﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Rest;

namespace Microsoft.Bot.Connector.Authentication
{
    /// <summary>
    /// A simple implementation of the <see cref="IServiceClientCredentialsFactory"/> interface.
    /// </summary>
    public class SimpleServiceClientCredentialFactory : IServiceClientCredentialsFactory
    {
        private HttpClient _httpClient;

        private ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="SimpleServiceClientCredentialFactory"/> class.
        /// with empty credentials.
        /// </summary>
        public SimpleServiceClientCredentialFactory()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SimpleServiceClientCredentialFactory"/> class.
        /// with the provided credentials.
        /// </summary>
        /// <param name="appId">The app ID.</param>
        /// <param name="password">The app password.</param>
        /// <param name="httpClient">A custom httpClient to use.</param>
        /// <param name="logger">A logger instance to use.</param>
        public SimpleServiceClientCredentialFactory(string appId, string password, HttpClient httpClient, ILogger logger)
        {
            AppId = appId;
            Password = password;
            _httpClient = httpClient;
            _logger = logger;
        }

        /// <summary>
        /// Gets or sets the app ID for this credential.
        /// </summary>
        /// <value>
        /// The app ID for this credential.
        /// </value>
        public string AppId { get; set; }

        /// <summary>
        /// Gets or sets the app password for this credential.
        /// </summary>
        /// <value>
        /// The app password for this credential.
        /// </value>
        public string Password { get; set; }

        /// <inheritdoc/>
        public Task<bool> IsValidAppIdAsync(string appId)
        {
            return Task.FromResult(appId == AppId);
        }

        /// <inheritdoc/>
        public Task<bool> IsAuthenticationDisabledAsync()
        {
            return Task.FromResult(string.IsNullOrEmpty(AppId));
        }

        /// <inheritdoc/>
        public Task<ServiceClientCredentials> CreateCredentialsAsync(string appId, string oauthScope, string loginEndpoint, bool validateAuthority)
        {
            if (loginEndpoint.StartsWith(AuthenticationConstants.ToChannelFromBotLoginUrlTemplate, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<ServiceClientCredentials>(
                    AppId == null
                        ?
                    MicrosoftAppCredentials.Empty
                        :
                    new MicrosoftAppCredentials(appId, Password, _httpClient, _logger, oauthScope));
            }
            else if (loginEndpoint.Equals(GovernmentAuthenticationConstants.ToChannelFromBotLoginUrl, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<ServiceClientCredentials>(
                    AppId == null
                        ?
                    MicrosoftGovernmentAppCredentials.Empty
                        :
                    new MicrosoftGovernmentAppCredentials(appId, Password, _httpClient, _logger, oauthScope));
            }
            else
            {
                return Task.FromResult<ServiceClientCredentials>(
                    AppId == null
                        ?
                    new PrivateCloudAppCredentials(null, null, null, null, null, loginEndpoint, validateAuthority)
                        :
                    new PrivateCloudAppCredentials(AppId, Password, _httpClient, _logger, oauthScope, loginEndpoint, validateAuthority));
            }
        }

        private class PrivateCloudAppCredentials : MicrosoftAppCredentials
        {
            private readonly string _oauthEndpoint;
            private readonly bool _validateAuthority;

            public PrivateCloudAppCredentials(string appId, string password, HttpClient customHttpClient, ILogger logger, string oAuthScope, string oauthEndpoint, bool validateAuthority)
            : base(appId, password, customHttpClient, logger, oAuthScope)
            {
                _oauthEndpoint = oauthEndpoint;
                _validateAuthority = validateAuthority;
            }

            public override string OAuthEndpoint
            {
                get { return _oauthEndpoint; }
            }

            public override bool ValidateAuthority
            {
                get { return _validateAuthority; }
            }
        }
    }
}
