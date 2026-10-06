using AutoMapper;
using SmartLost.AuthService.Api.Contracts;
using SmartLost.AuthService.Application.Commands.Login;
using SmartLost.AuthService.Application.Commands.Register;

namespace SmartLost.AuthService.Api.Mapping;

public sealed class AuthMappingProfile : Profile
{
    public AuthMappingProfile()
    {
        CreateMap<RegisterRequest, RegisterUserCommand>();
        CreateMap<LoginRequest, LoginUserCommand>();
    }
}
