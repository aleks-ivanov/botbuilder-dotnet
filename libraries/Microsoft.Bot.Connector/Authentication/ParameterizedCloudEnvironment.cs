﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Rest;

namespace Microsoft.Bot.Connector.Authentication
{
    internal class ParameterizedCloudEnvironment : ICloudEnvironment
    {
        private static HttpClient _defaultHttpClient = new HttpClient();

        private readonly string _channelService;
        private readonly bool _validateAuthority;
        private readonly string _toChannelFromBotLoginUrl;
        private readonly string _toChannelFromBotOAuthScope;
        private readonly string _toBotFromChannelTokenIssuer;
        private readonly string _oAuthUrl;
        private readonly string _toBotFromChannelOpenIdMetadataUrl;
        private readonly string _toBotFromEmulatorOpenIdMetadataUrl;
        private readonly string _callerId;
        private readonly IServiceClientCredentialsFactory _credentialFactory;
        private readonly AuthenticationConfiguration _authConfiguration;
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;

        public ParameterizedCloudEnvironment(
            string channelService,
            bool validateAuthority,
            string toChannelFromBotLoginUrl,
            string toChannelFromBotOAuthScope,
            string toBotFromChannelTokenIssuer,
            string oAuthUrl,
            string toBotFromChannelOpenIdMetadataUrl,
            string toBotFromEmulatorOpenIdMetadataUrl,
            string callerId,
            IServiceClientCredentialsFactory credentialFactory,
            AuthenticationConfiguration authConfiguration,
            HttpClient httpClient = null,
            ILogger logger = null)
        {
            _channelService = channelService;
            _validateAuthority = validateAuthority;
            _toChannelFromBotLoginUrl = toChannelFromBotLoginUrl;
            _toChannelFromBotOAuthScope = toChannelFromBotOAuthScope;
            _toBotFromChannelTokenIssuer = toBotFromChannelTokenIssuer;
            _oAuthUrl = oAuthUrl;
            _toBotFromChannelOpenIdMetadataUrl = toBotFromChannelOpenIdMetadataUrl;
            _toBotFromEmulatorOpenIdMetadataUrl = toBotFromEmulatorOpenIdMetadataUrl;
            _callerId = callerId;
            _credentialFactory = credentialFactory;
            _authConfiguration = authConfiguration;
            _httpClient = httpClient ?? _defaultHttpClient;
            _logger = logger;
        }

        public async Task<(ClaimsIdentity claimsIdentity, ServiceClientCredentials credentials, string scope, string callerId)> AuthenticateRequestAsync(Activity activity, string authHeader)
        {
            var claimsIdentity = await JwtTokenValidation_AuthenticateRequestAsync(activity, authHeader, _credentialFactory, _authConfiguration, _httpClient).ConfigureAwait(false);

            var scope = SkillValidation.IsSkillClaim(claimsIdentity.Claims) ? JwtTokenValidation.GetAppIdFromClaims(claimsIdentity.Claims) : _toChannelFromBotOAuthScope;

            var callerId = await GenerateCallerIdAsync(_credentialFactory, claimsIdentity).ConfigureAwait(false);

            var appId = BuiltinCloudEnvironment.GetAppId(claimsIdentity);

            var credentials = await _credentialFactory.CreateCredentialsAsync(appId, scope, _toChannelFromBotLoginUrl, _validateAuthority).ConfigureAwait(false);

            return (claimsIdentity, credentials, scope, callerId);
        }

        public Task<ServiceClientCredentials> GetProactiveCredentialsAsync(ClaimsIdentity claimsIdentity, string audience)
        {
            throw new NotImplementedException();
        }

        private async Task<string> GenerateCallerIdAsync(IServiceClientCredentialsFactory credentialFactory, ClaimsIdentity claimsIdentity)
        {
            // Is the bot accepting all incoming messages?
            if (await credentialFactory.IsAuthenticationDisabledAsync().ConfigureAwait(false))
            {
                // Return null so that the callerId is cleared.
                return null;
            }

            // Is the activity from another bot?
            if (SkillValidation.IsSkillClaim(claimsIdentity.Claims))
            {
                return $"{CallerIdConstants.BotToBotPrefix}{JwtTokenValidation.GetAppIdFromClaims(claimsIdentity.Claims)}";
            }

            return _callerId;
        }

        // The following code is based on JwtTokenValidation.AuthenticateRequest
        private async Task<ClaimsIdentity> JwtTokenValidation_AuthenticateRequestAsync(Activity activity, string authHeader, IServiceClientCredentialsFactory credentialFactory, AuthenticationConfiguration authConfiguration, HttpClient httpClient)
        {
            if (string.IsNullOrWhiteSpace(authHeader))
            {
                var isAuthDisabled = await credentialFactory.IsAuthenticationDisabledAsync().ConfigureAwait(false);
                if (isAuthDisabled)
                {
                    // In the scenario where Auth is disabled, we still want to have the
                    // IsAuthenticated flag set in the ClaimsIdentity. To do this requires
                    // adding in an empty claim.
                    return new ClaimsIdentity(new List<Claim>(), "anonymous");
                }

                // No Auth Header. Auth is required. Request is not authorized.
                throw new UnauthorizedAccessException();
            }

            var claimsIdentity = await JwtTokenValidation_ValidateAuthHeaderAsync(authHeader, credentialFactory, activity.ChannelId, authConfiguration, activity.ServiceUrl, httpClient).ConfigureAwait(false);

            AppCredentials.TrustServiceUrl(activity.ServiceUrl);

            return claimsIdentity;
        }

        private async Task<ClaimsIdentity> JwtTokenValidation_ValidateAuthHeaderAsync(string authHeader, IServiceClientCredentialsFactory credentialFactory, string channelId, AuthenticationConfiguration authConfiguration, string serviceUrl, HttpClient httpClient)
        {
            var identity = await JwtTokenValidation_AuthenticateTokenAsync(authHeader, credentialFactory, channelId, authConfiguration, serviceUrl, httpClient).ConfigureAwait(false);

            await JwtTokenValidation_ValidateClaimsAsync(authConfiguration, identity.Claims).ConfigureAwait(false);

            return identity;
        }

        private async Task JwtTokenValidation_ValidateClaimsAsync(AuthenticationConfiguration authConfig, IEnumerable<Claim> claims)
        {
            if (authConfig.ClaimsValidator != null)
            {
                // Call the validation method if defined (it should throw an exception if the validation fails)
                var claimsList = claims as IList<Claim> ?? claims.ToList();
                await authConfig.ClaimsValidator.ValidateClaimsAsync(claimsList).ConfigureAwait(false);
            }
            else if (SkillValidation.IsSkillClaim(claims))
            {
                throw new UnauthorizedAccessException("ClaimsValidator is required for validation of Skill Host calls.");
            }
        }

        private async Task<ClaimsIdentity> JwtTokenValidation_AuthenticateTokenAsync(string authHeader, IServiceClientCredentialsFactory credentialFactory, string channelId, AuthenticationConfiguration authConfiguration, string serviceUrl, HttpClient httpClient)
        {
            if (SkillValidation.IsSkillToken(authHeader))
            {
                return await SkillValidation_AuthenticateChannelTokenAsync(authHeader, credentialFactory, httpClient, channelId, authConfiguration).ConfigureAwait(false);
            }

            if (EmulatorValidation.IsTokenFromEmulator(authHeader))
            {
                return await EmulatorValidation_AuthenticateEmulatorTokenAsync(authHeader, credentialFactory, httpClient, channelId, authConfiguration).ConfigureAwait(false);
            }

            return await GovernmentChannelValidation_AuthenticateChannelTokenAsync(authHeader, credentialFactory, serviceUrl, httpClient, channelId, authConfiguration).ConfigureAwait(false);
        }

        // The following code is based on SkillValidation.AuthenticateChannelToken
        private async Task<ClaimsIdentity> SkillValidation_AuthenticateChannelTokenAsync(string authHeader, IServiceClientCredentialsFactory credentialFactory, HttpClient httpClient, string channelId, AuthenticationConfiguration authConfiguration)
        {
            var tokenValidationParameters =
                new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuers = new[]
                    {
                        "https://sts.windows.net/d6d49420-f39b-4df7-a1dc-d59a935871db/", // Auth v3.1, 1.0 token
                        "https://login.microsoftonline.com/d6d49420-f39b-4df7-a1dc-d59a935871db/v2.0", // Auth v3.1, 2.0 token
                        "https://sts.windows.net/f8cdef31-a31e-4b4a-93e4-5f571e91255a/", // Auth v3.2, 1.0 token
                        "https://login.microsoftonline.com/f8cdef31-a31e-4b4a-93e4-5f571e91255a/v2.0", // Auth v3.2, 2.0 token
                        "https://sts.windows.net/cab8a31a-1906-4287-a0d8-4eef66b95f6e/", // Auth for US Gov, 1.0 token
                        "https://login.microsoftonline.us/cab8a31a-1906-4287-a0d8-4eef66b95f6e/v2.0" // Auth for US Gov, 2.0 token
                    },
                    ValidateAudience = false, // Audience validation takes place manually in code.
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(5),
                    RequireSignedTokens = true
                };

            // TODO: what should the openIdMetadataUrl be here?
            var tokenExtractor = new JwtTokenExtractor(
                httpClient,
                tokenValidationParameters,
                _toBotFromEmulatorOpenIdMetadataUrl,
                AuthenticationConstants.AllowedSigningAlgorithms);

            var identity = await tokenExtractor.GetIdentityAsync(authHeader, channelId, authConfiguration.RequiredEndorsements).ConfigureAwait(false);

            await SkillValidation_ValidateIdentityAsync(identity, credentialFactory).ConfigureAwait(false);

            return identity;
        }

        private async Task SkillValidation_ValidateIdentityAsync(ClaimsIdentity identity, IServiceClientCredentialsFactory credentialFactory)
        {
            if (identity == null)
            {
                // No valid identity. Not Authorized.
                throw new UnauthorizedAccessException("Invalid Identity");
            }

            if (!identity.IsAuthenticated)
            {
                // The token is in some way invalid. Not Authorized.
                throw new UnauthorizedAccessException("Token Not Authenticated");
            }

            var versionClaim = identity.Claims.FirstOrDefault(c => c.Type == AuthenticationConstants.VersionClaim);
            if (versionClaim == null)
            {
                // No version claim
                throw new UnauthorizedAccessException($"'{AuthenticationConstants.VersionClaim}' claim is required on skill Tokens.");
            }

            // Look for the "aud" claim, but only if issued from the Bot Framework
            var audienceClaim = identity.Claims.FirstOrDefault(c => c.Type == AuthenticationConstants.AudienceClaim)?.Value;
            if (string.IsNullOrWhiteSpace(audienceClaim))
            {
                // Claim is not present or doesn't have a value. Not Authorized.
                throw new UnauthorizedAccessException($"'{AuthenticationConstants.AudienceClaim}' claim is required on skill Tokens.");
            }

            if (!await credentialFactory.IsValidAppIdAsync(audienceClaim).ConfigureAwait(false))
            {
                // The AppId is not valid. Not Authorized.
                throw new UnauthorizedAccessException("Invalid audience.");
            }

            var appId = JwtTokenValidation.GetAppIdFromClaims(identity.Claims);
            if (string.IsNullOrWhiteSpace(appId))
            {
                // Invalid appId
                throw new UnauthorizedAccessException("Invalid appId.");
            }
        }

        // The following code is based on EmulatorValidation.AuthenticateEmulatorToken
        private async Task<ClaimsIdentity> EmulatorValidation_AuthenticateEmulatorTokenAsync(string authHeader, IServiceClientCredentialsFactory credentialFactory, HttpClient httpClient, string channelId, AuthenticationConfiguration authConfiguration)
        {
            var toBotFromEmulatorTokenValidationParameters =
                new TokenValidationParameters()
                {
                    ValidateIssuer = true,
                    ValidIssuers = new[]
                    {
                        "https://sts.windows.net/d6d49420-f39b-4df7-a1dc-d59a935871db/",                    // Auth v3.1, 1.0 token
                        "https://login.microsoftonline.com/d6d49420-f39b-4df7-a1dc-d59a935871db/v2.0",      // Auth v3.1, 2.0 token
                        "https://sts.windows.net/f8cdef31-a31e-4b4a-93e4-5f571e91255a/",                    // Auth v3.2, 1.0 token
                        "https://login.microsoftonline.com/f8cdef31-a31e-4b4a-93e4-5f571e91255a/v2.0",      // Auth v3.2, 2.0 token
                        "https://sts.windows.net/cab8a31a-1906-4287-a0d8-4eef66b95f6e/",                    // Auth for US Gov, 1.0 token
                        "https://login.microsoftonline.us/cab8a31a-1906-4287-a0d8-4eef66b95f6e/v2.0", // Auth for US Gov, 2.0 token
                    },
                    ValidateAudience = false,   // Audience validation takes place manually in code.
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(5),
                    RequireSignedTokens = true,
                };

            var tokenExtractor = new JwtTokenExtractor(
                    httpClient,
                    toBotFromEmulatorTokenValidationParameters,
                    _toBotFromEmulatorOpenIdMetadataUrl,
                    AuthenticationConstants.AllowedSigningAlgorithms);

            var identity = await tokenExtractor.GetIdentityAsync(authHeader, channelId, authConfiguration.RequiredEndorsements).ConfigureAwait(false);
            if (identity == null)
            {
                // No valid identity. Not Authorized.
                throw new UnauthorizedAccessException("Invalid Identity");
            }

            if (!identity.IsAuthenticated)
            {
                // The token is in some way invalid. Not Authorized.
                throw new UnauthorizedAccessException("Token Not Authenticated");
            }

            // Now check that the AppID in the claimset matches
            // what we're looking for. Note that in a multi-tenant bot, this value
            // comes from developer code that may be reaching out to a service, hence the
            // Async validation.
            Claim versionClaim = identity.Claims.FirstOrDefault(c => c.Type == AuthenticationConstants.VersionClaim);
            if (versionClaim == null)
            {
                throw new UnauthorizedAccessException("'ver' claim is required on Emulator Tokens.");
            }

            string tokenVersion = versionClaim.Value;
            string appID = string.Empty;

            // The Emulator, depending on Version, sends the AppId via either the
            // appid claim (Version 1) or the Authorized Party claim (Version 2).
            if (string.IsNullOrWhiteSpace(tokenVersion) || tokenVersion == "1.0")
            {
                // either no Version or a version of "1.0" means we should look for
                // the claim in the "appid" claim.
                Claim appIdClaim = identity.Claims.FirstOrDefault(c => c.Type == AuthenticationConstants.AppIdClaim);
                if (appIdClaim == null)
                {
                    // No claim around AppID. Not Authorized.
                    throw new UnauthorizedAccessException("'appid' claim is required on Emulator Token version '1.0'.");
                }

                appID = appIdClaim.Value;
            }
            else if (tokenVersion == "2.0")
            {
                // Emulator, "2.0" puts the AppId in the "azp" claim.
                Claim appZClaim = identity.Claims.FirstOrDefault(c => c.Type == AuthenticationConstants.AuthorizedParty);
                if (appZClaim == null)
                {
                    // No claim around AppID. Not Authorized.
                    throw new UnauthorizedAccessException("'azp' claim is required on Emulator Token version '2.0'.");
                }

                appID = appZClaim.Value;
            }
            else
            {
                // Unknown Version. Not Authorized.
                throw new UnauthorizedAccessException($"Unknown Emulator Token version '{tokenVersion}'.");
            }

            if (!await credentialFactory.IsValidAppIdAsync(appID).ConfigureAwait(false))
            {
                throw new UnauthorizedAccessException($"Invalid AppId passed on token: {appID}");
            }

            return identity;
        }

        // The following code is based on GovernmentChannelValidation.AuthenticateChannelToken

        private async Task<ClaimsIdentity> GovernmentChannelValidation_AuthenticateChannelTokenAsync(string authHeader, IServiceClientCredentialsFactory credentialFactory, string serviceUrl, HttpClient httpClient, string channelId, AuthenticationConfiguration authConfig)
        {
            var tokenValidationParameters = GovernmentChannelValidation_GetTokenValidationParameters();

            var tokenExtractor = new JwtTokenExtractor(
                httpClient,
                tokenValidationParameters,
                _toBotFromChannelOpenIdMetadataUrl,
                AuthenticationConstants.AllowedSigningAlgorithms);

            var identity = await tokenExtractor.GetIdentityAsync(authHeader, channelId, authConfig.RequiredEndorsements).ConfigureAwait(false);

            await GovernmentChannelValidation_ValidateIdentityAsync(identity, credentialFactory, serviceUrl).ConfigureAwait(false);

            return identity;
        }

        private TokenValidationParameters GovernmentChannelValidation_GetTokenValidationParameters()
        {
            return new TokenValidationParameters()
            {
                ValidateIssuer = true,
                ValidIssuers = new[] { _toBotFromChannelTokenIssuer },

                // Audience validation takes place in JwtTokenExtractor
                ValidateAudience = false,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(5),
                RequireSignedTokens = true,
                ValidateIssuerSigningKey = true,
            };
        }

        private async Task GovernmentChannelValidation_ValidateIdentityAsync(ClaimsIdentity identity, IServiceClientCredentialsFactory credentialFactory, string serviceUrl)
        {
            if (identity == null)
            {
                // No valid identity. Not Authorized.
                throw new UnauthorizedAccessException();
            }

            if (!identity.IsAuthenticated)
            {
                // The token is in some way invalid. Not Authorized.
                throw new UnauthorizedAccessException();
            }

            // Now check that the AppID in the claimset matches
            // what we're looking for. Note that in a multi-tenant bot, this value
            // comes from developer code that may be reaching out to a service, hence the
            // Async validation.

            // Look for the "aud" claim, but only if issued from the Bot Framework
            var audienceClaim = identity.Claims.FirstOrDefault(
                c => c.Issuer == _toBotFromChannelTokenIssuer && c.Type == AuthenticationConstants.AudienceClaim);

            if (audienceClaim == null)
            {
                // The relevant audience Claim MUST be present. Not Authorized.
                throw new UnauthorizedAccessException();
            }

            // The AppId from the claim in the token must match the AppId specified by the developer.
            // In this case, the token is destined for the app, so we find the app ID in the audience claim.
            var appIdFromClaim = audienceClaim.Value;
            if (string.IsNullOrWhiteSpace(appIdFromClaim))
            {
                // Claim is present, but doesn't have a value. Not Authorized.
                throw new UnauthorizedAccessException();
            }

            if (!await credentialFactory.IsValidAppIdAsync(appIdFromClaim).ConfigureAwait(false))
            {
                // The AppId is not valid. Not Authorized.
                throw new UnauthorizedAccessException($"Invalid AppId passed on token: {appIdFromClaim}");
            }

            if (serviceUrl != null)
            {
                var serviceUrlClaim = identity.Claims.FirstOrDefault(claim => claim.Type == AuthenticationConstants.ServiceUrlClaim)?.Value;
                if (string.IsNullOrWhiteSpace(serviceUrlClaim))
                {
                    // Claim must be present. Not Authorized.
                    throw new UnauthorizedAccessException();
                }

                if (!string.Equals(serviceUrlClaim, serviceUrl, StringComparison.OrdinalIgnoreCase))
                {
                    // Claim must match. Not Authorized.
                    throw new UnauthorizedAccessException();
                }
            }
        }
    }
}
