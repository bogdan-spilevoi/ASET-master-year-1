using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SmartLost.AuthService.Api.Contracts;
using SmartLost.AuthService.Api.Controllers;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.AuthService.Application.Queries.GetCurrentUser;
using SmartLost.BuildingBlocks.Application.Behaviors;
using SmartLost.BuildingBlocks.Application.Caching;
using SmartLost.BuildingBlocks.Application.Profiling;
using SmartLost.BuildingBlocks.Core.Results;
using Xunit;

namespace SmartLost.AuthService.UnitTests;

public sealed class AuthorizationEndpointTests
{
    [Fact]
    public async Task AttributeProtectedQueryRejectsDirectAnonymousMediatorDispatch()
    {
        using AuthApiFactory factory = new();
        using IServiceScope scope = factory.Services.CreateScope();
        ISender sender = scope.ServiceProvider.GetRequiredService<ISender>();
        Type[] expectedOrder = [typeof(ProfilingBehavior<,>), typeof(ExceptionToResultBehavior<,>),
            typeof(AuthorizationBehavior<,>), typeof(ValidationBehavior<,>), typeof(CachingBehavior<,>), typeof(TransactionBehavior<,>)];
        Assert.Equal(expectedOrder, scope.ServiceProvider
            .GetServices<IPipelineBehavior<GetCurrentUserQuery, Result<CurrentUserDetails>>>()
            .Select(behavior => behavior.GetType().GetGenericTypeDefinition()));
        Assert.Empty(typeof(GetCurrentUserQuery).GetCustomAttributes(typeof(CacheableQueryAttribute), true));

        Result<CurrentUserDetails> result = await sender.Send(new GetCurrentUserQuery());

        Error error = Assert.Single(result.Errors);
        Assert.Equal("authorization.unauthenticated", error.Code);
        Assert.Equal(ErrorKind.Unauthorized, error.Kind);
        Assert.Null(scope.ServiceProvider.GetRequiredService<IProfilingContext>().Current);
    }

    [Fact]
    public async Task CurrentIdentityIsReadForEachCallerWithCachingBehaviorRegistered()
    {
        using AuthApiFactory factory = new();
        using HttpClient first = factory.CreateClient();
        using HttpClient second = factory.CreateClient();
        string suffix = Guid.NewGuid().ToString("N");
        var identities = new AuthResponse[2];
        HttpClient[] clients = [first, second];
        for (int index = 0; index < clients.Length; index++)
        {
            using HttpResponseMessage registration = await clients[index].PostAsJsonAsync("/api/auth/register", new
            {
                userName = $"user{index}{suffix}",
                email = $"user{index}{suffix}@example.com",
                password = "ValidPass123!"
            });
            registration.EnsureSuccessStatusCode();
            identities[index] = (await registration.Content.ReadFromJsonAsync<AuthResponse>())!;
            clients[index].DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", identities[index].AccessToken);
        }

        foreach (int index in new[] { 0, 1, 0 })
        {
            CurrentUserResponse? identity = await clients[index].GetFromJsonAsync<CurrentUserResponse>("/api/auth/me");
            Assert.Equal(identities[index].UserId, identity!.UserId);
            Assert.Equal(identities[index].Email, identity.Email);
        }
    }

    [Fact]
    public async Task MeEndpointReturns401WhenUnauthenticated()
    {
        using AuthApiFactory factory = new();
        using HttpClient client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        HttpResponseMessage response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MeEndpointUsesTheAttributeProtectedApplicationQuery()
    {
        using AuthApiFactory factory = new();
        using HttpClient client = factory.CreateClient();
        HttpResponseMessage registerResponse = await client.PostAsJsonAsync("/api/auth/register", new
        {
            userName = "authorized-user",
            email = "authorized@smartlost.local",
            password = "AuthorizedPass123!"
        });
        AuthResponse auth = await registerResponse.Content.ReadFromJsonAsync<AuthResponse>()
            ?? throw new InvalidOperationException("Register response was empty.");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        HttpResponseMessage response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        CurrentUserResponse? currentUser = await response.Content.ReadFromJsonAsync<CurrentUserResponse>();
        Assert.NotNull(currentUser);
        Assert.Equal(auth.UserId, currentUser.UserId);
        Assert.Equal(auth.UserName, currentUser.UserName);
        Assert.Equal(auth.Email, currentUser.Email);
        Assert.True(currentUser.IsAuthenticated);
    }

    [Fact]
    public async Task MeEndpointReturnsFailureActionResultWhenQueryFails()
    {
        ISender mockSender = new FailingSender();
        var controller = new AuthController(mockSender, null!)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        IActionResult result = await controller.Me(CancellationToken.None);

        Assert.IsType<ObjectResult>(result);
    }

    private sealed class FailingSender : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (typeof(TResponse) == typeof(Result<CurrentUserDetails>))
            {
                var failure = Result<CurrentUserDetails>.Failure(
                    new Error("test.error", "Failure description.", ErrorKind.Unauthorized));
                return Task.FromResult((TResponse)(object)failure);
            }

            throw new NotImplementedException();
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
        {
            throw new NotImplementedException();
        }
    }
}
