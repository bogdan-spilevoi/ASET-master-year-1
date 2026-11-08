using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SmartLost.AuthService.Api.Contracts;
using SmartLost.AuthService.Application.Contracts;
using SmartLost.AuthService.Application.Queries.GetCurrentUser;
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

        Result<CurrentUserDetails> result = await sender.Send(new GetCurrentUserQuery());

        Error error = Assert.Single(result.Errors);
        Assert.Equal("authorization.unauthenticated", error.Code);
        Assert.Equal(ErrorKind.Unauthorized, error.Kind);
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
}
