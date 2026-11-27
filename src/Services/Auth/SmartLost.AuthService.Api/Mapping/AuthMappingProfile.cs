using AutoMapper;
using SmartLost.AuthService.Api.Contracts;
using SmartLost.AuthService.Application.Commands.ConfirmEmail;
using SmartLost.AuthService.Application.Commands.Login;
using SmartLost.AuthService.Application.Commands.Logout;
using SmartLost.AuthService.Application.Commands.Refresh;
using SmartLost.AuthService.Application.Commands.Register;

namespace SmartLost.AuthService.Api.Mapping;

public sealed class AuthMappingProfile : Profile
{
    public AuthMappingProfile()
    {
        CreateMap<RegisterRequest, RegisterUserCommand>();
        CreateMap<LoginRequest, LoginUserCommand>();
        CreateMap<RefreshRequest, RefreshTokenCommand>();
        CreateMap<RefreshRequest, LogoutCommand>();
        CreateMap<ConfirmEmailRequest, ConfirmEmailCommand>();
    }
}
