﻿using System;
using System.Net.Http;
using System.Threading.Tasks;
using DotNetCore.CAP;
using IdentityModel.Client;
using IdentityServer4.Configuration;
using IdentityServer4.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using TT.Abp.WeixinManagement.Application.Dtos;
using TT.Abp.WeixinManagement.Domain;
using TT.Extensions;
using Volo.Abp.Application.Services;
using Volo.Abp.Domain.Repositories;
using Volo.Abp.Identity;
using Volo.Abp.MultiTenancy;
using Volo.Abp.Settings;
using Volo.Abp.Uow;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace TT.Abp.WeixinManagement.Application
{
    public class WeixinAppService : ApplicationService, IWeixinAppService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IPasswordHasher<IdentityUser> _passwordHasher;
        private readonly ICurrentTenant _currentTenant;
        private readonly ISettingProvider _setting;
        private readonly WeixinManager _weixinManager;
        private readonly IdentityUserStore _identityUserStore;
        private readonly ICapPublisher _capBus;
        private readonly IUserClaimsPrincipalFactory<IdentityUser> _principalFactory;
        private readonly IdentityServerOptions _options;
        private readonly ITokenService _ts;
        private readonly IUnitOfWorkManager _unitOfWorkManager;


        public WeixinAppService(
            IHttpClientFactory httpClientFactory,
            IPasswordHasher<IdentityUser> passwordHasher,
            ICurrentTenant currentTenant,
            ISettingProvider setting,
            WeixinManager weixinManager,
            IdentityUserStore identityUserStore,
            ICapPublisher capBus,
            IUserClaimsPrincipalFactory<IdentityUser> principalFactory,
            IdentityServerOptions options,
            ITokenService TS,
            IUnitOfWorkManager unitOfWorkManager
        )
        {
            ObjectMapperContext = typeof(WeixinManagementModule);
            _httpClientFactory = httpClientFactory;
            _passwordHasher = passwordHasher;
            _currentTenant = currentTenant;
            _setting = setting;
            _weixinManager = weixinManager;
            _identityUserStore = identityUserStore;
            _capBus = capBus;
            _principalFactory = principalFactory;
            _options = options;
            _ts = TS;
            _unitOfWorkManager = unitOfWorkManager;
        }

        public async Task<object> Code2Session(WeChatMiniProgramAuthenticateModel loginModel)
        {
            return await Task.FromResult<object>(null);
        }


        public async Task<string> GetAccessToken(string appid)
        {
            var appId = await _setting.GetOrNullAsync(WeixinManagementSetting.MiniAppId);
            var appSec = await _setting.GetOrNullAsync(WeixinManagementSetting.MiniAppSecret);

            var token = await _weixinManager.GetAccessTokenAsync(appId, appSec);

            return token;
        }

        [HttpPost]
        public async Task<object> MiniAuth(WeChatMiniProgramAuthenticateModel loginModel)
        {
            var appId = await _setting.GetOrNullAsync(WeixinManagementSetting.MiniAppId);
            var appSec = await _setting.GetOrNullAsync(WeixinManagementSetting.MiniAppSecret);

            var session = await _weixinManager.Mini_Code2Session(loginModel.code, appId, appSec);

            // 解密用户信息
            var miniUserInfo =
                await _weixinManager.Mini_GetUserInfo(appId, loginModel.encryptedData, session.session_key, loginModel.iv);

            // 更新数据库
            await _capBus.PublishAsync("weixin.services.mini.getuserinfo", miniUserInfo);
            var token = "";

            var user = await _identityUserStore.FindByLoginAsync($"{appId}_unionid", miniUserInfo.unionid);
            if (user == null)
            {
                var userId = Guid.NewGuid();
                user = new IdentityUser(userId, miniUserInfo.unionid, $"{miniUserInfo.unionid}@somall.top", _currentTenant.Id);

                using (var uow = _unitOfWorkManager.Begin())
                {
                    var passHash = _passwordHasher.HashPassword(user, "1q2w3E*");
                    await _identityUserStore.CreateAsync(user);
                    await _identityUserStore.SetPasswordHashAsync(user, passHash);
                    await _identityUserStore.AddLoginAsync(user, new UserLoginInfo($"{appId}_unionid", miniUserInfo.unionid, "unionid"));
                    await _identityUserStore.AddLoginAsync(user, new UserLoginInfo($"{appId}_openid", miniUserInfo.openid, "openid"));

                    await _unitOfWorkManager.Current.SaveChangesAsync();
                    await uow.CompleteAsync();
                    return await Task.FromResult(new
                    {
                        AccessToken = "retry",
                        ExternalUser = miniUserInfo,
                        SessionKey = session.session_key
                    });
                }
            }

            var serverClient = _httpClientFactory.CreateClient();
            var disco = await serverClient.GetDiscoveryDocumentAsync("https://localhost:44380");

            var result = await serverClient.RequestTokenAsync(
                new TokenRequest
                {
                    Address = disco.TokenEndpoint,
                    GrantType = "password",

                    ClientId = "SoMall_App",
                    ClientSecret = "1q2w3e*",
                    Parameters =
                    {
                        {"UserName", user.UserName},
                        {"Password", "1q2w3E*"},
                        {"scope", "SoMall"}
                    }
                });
            token = result.AccessToken;

            return await Task.FromResult(new
            {
                AccessToken = token,
                ExternalUser = miniUserInfo,
                SessionKey = session.session_key
            });
        }


        [HttpGet]
        [Authorize]
        public async Task<object> GetUnLimitQr(Guid scene, string page = null)
        {
            var shorter = scene.ToShortString();
            return new {url = await _weixinManager.Getwxacodeunlimit(shorter, page)};
        }

        // public async Task<TokenResponse> DelegateAsync(string username)
        // {
        //     var serverClient = _httpClientFactory.CreateClient();
        //     var disco = await serverClient.GetDiscoveryDocumentAsync("https://localhost:44380");
        //
        //     //send custom grant to token endpoint, return response
        //     return await serverClient.RequestTokenAsync(
        //         new TokenRequest
        //         {
        //             Address = disco.TokenEndpoint,
        //             GrantType = "password",
        //
        //             ClientId = "SoMall_App",
        //             ClientSecret = "1q2w3e*",
        //             Parameters =
        //             {
        //                 {"UserName", username},
        //                 {"Password", "1q2w3E*"},
        //                 {"scope", "SoMall"}
        //             }
        //         });
        // }

        //
        // public async Task<string> LoginAs(IdentityUser user)
        // {
        //     var Request = new TokenCreationRequest();
        //     var IdentityPricipal = await _principalFactory.CreateAsync(user);
        //     var IdentityUser = new IdentityServerUser(user.Id.ToString());
        //     IdentityUser.AdditionalClaims = IdentityPricipal.Claims.ToArray();
        //     IdentityUser.DisplayName = user.UserName;
        //     IdentityUser.AuthenticationTime = System.DateTime.UtcNow;
        //     IdentityUser.IdentityProvider = IdentityServerConstants.LocalIdentityProvider;
        //     Request.Subject = IdentityUser.CreatePrincipal();
        //     Request.IncludeAllIdentityClaims = true;
        //     Request.ValidatedRequest = new ValidatedRequest();
        //     Request.ValidatedRequest.Subject = Request.Subject;
        //     Request.ValidatedRequest.SetClient(GetClient());
        //     Request.Resources = new Resources(GetIdentityResources(), GetApiResources());
        //     Request.ValidatedRequest.Options = _options;
        //     Request.ValidatedRequest.ClientClaims = IdentityUser.AdditionalClaims;
        //     var Token = await _ts.CreateAccessTokenAsync(Request);
        //     Token.Issuer = "http://localhost:44380";
        //     var TokenValue = await _ts.CreateSecurityTokenAsync(Token);
        //     return TokenValue;
        // }
    }
}