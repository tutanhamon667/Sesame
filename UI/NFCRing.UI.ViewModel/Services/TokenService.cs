﻿using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NFCRing.Service.Common;

namespace NFCRing.UI.ViewModel.Services
{
    public class TokenService : ITokenService
    {
        private readonly ILogger _logger;

        public TokenService(ILogger logger)
        {
            _logger = logger;
        }

        public async Task<Dictionary<string, string>> GetTokensAsync(string userName)
        {
            TcpClient client = null;

            ServiceCommunication.SendNetworkMessage(ref client, JsonConvert.SerializeObject(new NetworkMessage(MessageType.GetState) { Username = userName }));

            var response = await Task<string>.Factory.StartNew(() =>
            {
                return ServiceCommunication.ReadNetworkMessage(ref client);
            });

            if (string.IsNullOrEmpty(response))
                return null;
            
            UserServerState userServerState = JsonConvert.DeserializeObject<UserServerState>(response);

            _logger.Debug($"GetTokensAsync: {JsonConvert.SerializeObject(userServerState.UserConfiguration.Tokens)}");

            return userServerState.UserConfiguration.Tokens;
        }

        public async Task RemoveTokenAsync(string token)
        {
            TcpClient client = null;

            ServiceCommunication.SendNetworkMessage(ref client,
                JsonConvert.SerializeObject(new NetworkMessage(MessageType.Delete) {Token = token, Username = CurrentUser.Get()}));

            _logger.Debug($"RemoveTokenAsync: {token}");

            await Task.Yield();
        }

        public async Task AddTokenAsync(string userName, string password, string token)
        {
            TcpClient client = null;

            var ringName = await GetRingNameAsync(userName);

            await Task.Factory.StartNew(() =>
            {
                ServiceCommunication.SendNetworkMessage(ref client,
                    JsonConvert.SerializeObject(new NetworkMessage(MessageType.RegisterToken)
                    {
                        TokenFriendlyName = ringName,
                        Username = userName,
                        Password = password,
                        Token = token
                    }));
            });

            _logger.Debug($"AddTokenAsync: username: {userName} token: {token}");
        }

        public async Task<string> GetNewTokenAsync(CancellationToken cancellationToken)
        {
            var newToken = await Task.Factory.StartNew(() =>
            {
                return GetNewToken(cancellationToken);
            }, cancellationToken);

            _logger.Debug($"GetNewTokenAsync: {newToken}");

            return newToken;
        }

        private string GetNewToken(CancellationToken cancellationToken)
        {
            TcpClient client = null;

            var i = 0;

            while (true)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                var message = ServiceCommunication.ReadNetworkMessage(ref client);
                if (!string.IsNullOrEmpty(message))
                {
                    _logger.Debug($"GetNewToken: {message}");
                    var networkMessage = JsonConvert.DeserializeObject<NetworkMessage>(message);
                    return networkMessage?.Token;
                }

#if DEBUG
                i++;

                if (i > 10)
                    return "234423456";

                Thread.Sleep(200);
#endif

                Thread.Sleep(50);
            }

            return null;
        }

        private async Task<string> GetRingNameAsync(string login)
        {
            var oldTokens = await GetTokensAsync(login);

            var id = "00";

            for (var i = 1; i < CurrentUser.MaxTokensCount; i++)
            {
                var name = i.ToString("00");

                if (!oldTokens.Values.Any(x => x.Contains(name)))
                {
                    id = name;
                    break;
                }
            }

            return $"ring {id}";
        }
    }
}