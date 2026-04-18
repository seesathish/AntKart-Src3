using AK.BuildingBlocks.Authentication;
using AK.UserIdentity.API.DTOs;
using AK.UserIdentity.API.Services;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using System.Net;
using System.Text.Json;

namespace AK.UserIdentity.Tests.Services;

public sealed class KeycloakServiceTests
{
    private readonly KeycloakSettings _settings = new()
    {
        Authority = "http://localhost:8090/realms/antkart",
        Audience = "antkart-client",
        AdminUrl = "http://localhost:8090",
        Realm = "antkart",
        ClientId = "antkart-client",
        ClientSecret = "antkart-secret"
    };

    private IHttpClientFactory CreateHttpClientFactory(HttpResponseMessage response)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        var client = new HttpClient(handlerMock.Object);
        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(client);
        return factoryMock.Object;
    }

    [Fact]
    public async Task LoginAsync_WithValidCredentials_ReturnsTokenResponse()
    {
        var tokenJson = JsonSerializer.Serialize(new
        {
            access_token = "access.token.value",
            refresh_token = "refresh.token.value",
            expires_in = 300,
            token_type = "Bearer"
        });

        var factory = CreateHttpClientFactory(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(tokenJson, System.Text.Encoding.UTF8, "application/json")
        });

        var svc = new KeycloakService(factory, Options.Create(_settings));
        var result = await svc.LoginAsync("user1", "pass1");

        result.Should().NotBeNull();
        result.AccessToken.Should().Be("access.token.value");
        result.RefreshToken.Should().Be("refresh.token.value");
        result.ExpiresIn.Should().Be(300);
        result.TokenType.Should().Be("Bearer");
    }

    [Fact]
    public async Task LoginAsync_WithInvalidCredentials_ThrowsUnauthorizedAccessException()
    {
        var factory = CreateHttpClientFactory(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":\"invalid_grant\"}", System.Text.Encoding.UTF8, "application/json")
        });

        var svc = new KeycloakService(factory, Options.Create(_settings));
        var act = async () => await svc.LoginAsync("bad", "creds");

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task RefreshTokenAsync_WithValidToken_ReturnsNewTokens()
    {
        var tokenJson = JsonSerializer.Serialize(new
        {
            access_token = "new.access.token",
            refresh_token = "new.refresh.token",
            expires_in = 300,
            token_type = "Bearer"
        });

        var factory = CreateHttpClientFactory(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(tokenJson, System.Text.Encoding.UTF8, "application/json")
        });

        var svc = new KeycloakService(factory, Options.Create(_settings));
        var result = await svc.RefreshTokenAsync("old.refresh.token");

        result.AccessToken.Should().Be("new.access.token");
    }

    [Fact]
    public async Task RefreshTokenAsync_WithInvalidToken_ThrowsUnauthorizedAccessException()
    {
        var factory = CreateHttpClientFactory(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":\"invalid_grant\"}", System.Text.Encoding.UTF8, "application/json")
        });

        var svc = new KeycloakService(factory, Options.Create(_settings));
        var act = async () => await svc.RefreshTokenAsync("bad.token");

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task GetUserInfoAsync_WithValidToken_ReturnsUserInfo()
    {
        var userInfoJson = JsonSerializer.Serialize(new
        {
            sub = "user-id-123",
            preferred_username = "john.doe",
            email = "john@example.com",
            given_name = "John",
            family_name = "Doe",
            realm_access = new { roles = new[] { "user" } }
        });

        var factory = CreateHttpClientFactory(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(userInfoJson, System.Text.Encoding.UTF8, "application/json")
        });

        var svc = new KeycloakService(factory, Options.Create(_settings));
        var result = await svc.GetUserInfoAsync("valid.access.token");

        result.Id.Should().Be("user-id-123");
        result.Username.Should().Be("john.doe");
        result.Email.Should().Be("john@example.com");
        result.Roles.Should().Contain("user");
    }

    [Fact]
    public async Task GetUserInfoAsync_WithInvalidToken_ThrowsUnauthorizedAccessException()
    {
        var factory = CreateHttpClientFactory(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("{\"error\":\"invalid_token\"}", System.Text.Encoding.UTF8, "application/json")
        });

        var svc = new KeycloakService(factory, Options.Create(_settings));
        var act = async () => await svc.GetUserInfoAsync("invalid.token");

        await act.Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
