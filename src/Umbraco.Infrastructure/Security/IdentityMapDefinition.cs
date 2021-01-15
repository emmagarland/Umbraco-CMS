using System;
using Microsoft.Extensions.Options;
using Umbraco.Core;
using Umbraco.Core.Configuration.Models;
using Umbraco.Core.Mapping;
using Umbraco.Core.Models;
using Umbraco.Core.Models.Membership;
using Umbraco.Core.Services;

namespace Umbraco.Infrastructure.Security
{
    public class IdentityMapDefinition : IMapDefinition
    {
        private readonly ILocalizedTextService _textService;
        private readonly IEntityService _entityService;
        private readonly GlobalSettings _globalSettings;

        public IdentityMapDefinition(ILocalizedTextService textService, IEntityService entityService, IOptions<GlobalSettings> globalSettings)
        {
            _textService = textService;
            _entityService = entityService;
            _globalSettings = globalSettings.Value;
        }

        public void DefineMaps(UmbracoMapper mapper)
        {
            mapper.Define<IUser, BackOfficeIdentityUser>(
                (source, context) =>
                {
                    var target = new BackOfficeIdentityUser(_globalSettings, source.Id, source.Groups);
                    target.DisableChangeTracking();
                    return target;
                },
                (source, target, context) =>
                {
                    Map(source, target);
                    target.ResetDirtyProperties(true);
                    target.EnableChangeTracking();
                });

            mapper.Define<IMember, MembersIdentityUser>(
                (source, context) =>
                {
                    var target = new MembersIdentityUser(source.Id);
                    target.DisableChangeTracking();
                    return target;
                },
                (source, target, context) =>
                {
                    Map(source, target);
                    target.ResetDirtyProperties(true);
                    target.EnableChangeTracking();
                });
        }

        // Umbraco.Code.MapAll -Id -Groups -LockoutEnabled -PhoneNumber -PhoneNumberConfirmed -TwoFactorEnabled
        private void Map(IUser source, BackOfficeIdentityUser target)
        {
            // well, the ctor has been fixed
            /*
            // these two are already set in ctor but BackOfficeIdentityUser ctor is CompletelyBroken
            target.Id = source.Id;
            target.Groups = source.Groups.ToArray();
            */

            target.CalculatedMediaStartNodeIds = source.CalculateMediaStartNodeIds(_entityService);
            target.CalculatedContentStartNodeIds = source.CalculateContentStartNodeIds(_entityService);
            target.Email = source.Email;
            target.UserName = source.Username;
            target.LastPasswordChangeDateUtc = source.LastPasswordChangeDate.ToUniversalTime();
            target.LastLoginDateUtc = source.LastLoginDate.ToUniversalTime();
            target.EmailConfirmed = source.EmailConfirmedDate.HasValue;
            target.Name = source.Name;
            target.AccessFailedCount = source.FailedPasswordAttempts;
            target.PasswordHash = GetPasswordHash(source.RawPasswordValue);
            target.PasswordConfig = source.PasswordConfiguration;
            target.StartContentIds = source.StartContentIds;
            target.StartMediaIds = source.StartMediaIds;
            target.Culture = source.GetUserCulture(_textService, _globalSettings).ToString(); // project CultureInfo to string
            target.IsApproved = source.IsApproved;
            target.SecurityStamp = source.SecurityStamp;
            target.LockoutEnd = source.IsLockedOut ? DateTime.MaxValue.ToUniversalTime() : (DateTime?)null;

            // this was in AutoMapper but does not have a setter anyways
            //target.AllowedSections = source.AllowedSections.ToArray(),

            // these were marked as ignored for AutoMapper but don't have a setter anyways
            //target.Logins =;
            //target.Claims =;
            //target.Roles =;
        }

        private void Map(IMember source, MembersIdentityUser target)
        {
            target.Email = source.Email;
            target.UserName = source.Username;
            target.LastPasswordChangeDateUtc = source.LastPasswordChangeDate.ToUniversalTime();
            target.LastLoginDateUtc = source.LastLoginDate.ToUniversalTime();
            //target.EmailConfirmed = source.EmailConfirmedDate.HasValue;
            target.Name = source.Name;
            target.AccessFailedCount = source.FailedPasswordAttempts;
            target.PasswordHash = GetPasswordHash(source.RawPasswordValue);
            target.PasswordConfig = source.PasswordConfiguration;
            target.IsApproved = source.IsApproved;
            //target.SecurityStamp = source.SecurityStamp;
            target.LockoutEnd = source.IsLockedOut ? DateTime.MaxValue.ToUniversalTime() : (DateTime?)null;
        }

        private static string GetPasswordHash(string storedPass)
        {
            return storedPass.StartsWith(Constants.Security.EmptyPasswordPrefix) ? null : storedPass;
        }
    }
}