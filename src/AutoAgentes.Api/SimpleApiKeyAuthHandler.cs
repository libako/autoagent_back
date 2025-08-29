using System;
using System.Linq;
using System.Security.Claims;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace AutoAgentes.Api;

public class SimpleApiKeyAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
	public SimpleApiKeyAuthHandler(
		IOptionsMonitor<AuthenticationSchemeOptions> options,
		ILoggerFactory logger,
		UrlEncoder encoder,
		ISystemClock clock) : base(options, logger, encoder, clock) { }

	protected override Task<AuthenticateResult> HandleAuthenticateAsync()
	{
		var mode = Context.RequestServices.GetRequiredService<IConfiguration>()["Auth:Mode"];
		if (!string.Equals(mode, "ApiKey", StringComparison.OrdinalIgnoreCase))
			return Task.FromResult(AuthenticateResult.NoResult());

		var provided = Request.Headers["x-api-key"].FirstOrDefault();
		if (string.IsNullOrEmpty(provided))
			return Task.FromResult(AuthenticateResult.Fail("Missing API key"));

		var allowed = Context.RequestServices.GetRequiredService<IConfiguration>()
			.GetSection("Auth:ApiKeys").Get<string[]>() ?? Array.Empty<string>();
		if (!allowed.Contains(provided))
			return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));

		var claims = new List<Claim>
		{
			new Claim(ClaimTypes.NameIdentifier, provided),
			new Claim(ClaimTypes.Name, "api-key")
		}.ToArray();
		var identity = new ClaimsIdentity(claims as IEnumerable<Claim>, Scheme.Name);
		var principal = new ClaimsPrincipal(identity);
		var ticket = new AuthenticationTicket(principal, Scheme.Name);
		return Task.FromResult(AuthenticateResult.Success(ticket));
	}
}
