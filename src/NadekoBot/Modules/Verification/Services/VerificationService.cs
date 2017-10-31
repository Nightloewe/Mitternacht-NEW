﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GommeHDnetForumAPI;
using Mitternacht.Common.Collections;
using Mitternacht.Services;
using Mitternacht.Services.Database.Models;
using NLog;

namespace Mitternacht.Modules.Verification.Services
{
    public class VerificationService : INService
    {
        private readonly IBotCredentials _creds;
        private readonly DbService _db;
        public readonly Logger Log;

        private readonly Random _rnd = new Random();

        public Forum Forum { get; private set; }
        public bool Enabled => Forum.LoggedIn;

        public readonly ConcurrentHashSet<ValidationKey> ValidationKeys = new ConcurrentHashSet<ValidationKey>();

        public VerificationService(IBotCredentials creds, DbService db) {
            _creds = creds;
            _db = db;
            Log = LogManager.GetCurrentClassLogger();
            InitForumInstance();
        }

        public void InitForumInstance() {
            Forum = new Forum(_creds.ForumUsername, _creds.ForumPassword);
            Log.Log(Forum.LoggedIn ? LogLevel.Info : LogLevel.Warn, $"Initialized new Forum instance. Login {(Forum.LoggedIn ? "successful" : "failed, ignoring verification actions")}!");
        }

        private string GenerateKey() {
            var bytes = new byte[32];
            _rnd.NextBytes(bytes);
            return Convert.ToBase64String(bytes, Base64FormattingOptions.None);
        }

        public string GetKey(KeyScope keyscope, long forumuserid, ulong userid, ulong guildid) {
            ValidationKey key;
            while (ValidationKeys.Contains(key = new ValidationKey(GenerateKey(), keyscope, forumuserid, userid, guildid))) ;
            Task.Run(async () => {
                await Task.Delay(600000);
                ValidationKeys.TryRemove(key);
            });
            ValidationKeys.Add(key);
            return key.Key;
        }

        public IEnumerable<VerificatedUser> GetVerifiedUsers(ulong guildId) {
            using (var uow = _db.UnitOfWork)
                return uow.VerificatedUser.GetVerificatedUsers(guildId);
        }

        public int GetVerifiedUserCount(ulong guildId) {
            using (var uow = _db.UnitOfWork)
                return uow.VerificatedUser.GetCount(guildId);
        }

        public bool CanVerifyForumAccount(ulong guildId, ulong userId, long forumUserId) {
            using (var uow = _db.UnitOfWork) {
                return uow.VerificatedUser.IsForumUserIndependentFromDiscordUser(guildId, userId, forumUserId);
            }
        }

        public async Task SetVerifiedRole(ulong guildId, ulong? roleId)
        {
            using (var uow = _db.UnitOfWork)
            {
                uow.GuildConfigs.For(guildId, set => set).VerifiedRoleId = roleId;
                await uow.CompleteAsync();
            }
        }

        public ulong? GetVerifiedRoleId(ulong guildId)
        {
            using (var uow = _db.UnitOfWork)
            {
                return uow.GuildConfigs.For(guildId, set => set).VerifiedRoleId;
            }
        }

        public async Task SetVerifyString(ulong guildId, string verifystring)
        {
            using (var uow = _db.UnitOfWork) {
                uow.GuildConfigs.For(guildId, set => set).VerifyString = verifystring;
                await uow.CompleteAsync();
            }
        }

        public string GetVerifyString(ulong guildId)
        {
            using (var uow = _db.UnitOfWork) return uow.GuildConfigs.For(guildId, set => set).VerifyString;
        }

        public string GetVerificationTutorialText(ulong guildId) {
            using (var uow = _db.UnitOfWork) return uow.GuildConfigs.For(guildId, set => set).VerificationTutorialText;
        }

        public async Task SetVerificationTutorialText(ulong guildId, string text) {
            using (var uow = _db.UnitOfWork) {
                uow.GuildConfigs.For(guildId, set => set).VerificationTutorialText = text;
                await uow.CompleteAsync();
            }
        }

        public class ValidationKey
        {
            public string Key { get; }
            public KeyScope KeyScope { get; }
            public long ForumUserId { get; }
            public ulong DiscordUserId { get; }
            public ulong GuildId { get; }

            public ValidationKey(string key, KeyScope keyscope, long forumuserid, ulong userid, ulong guildid) {
                Key = key;
                KeyScope = keyscope;
                ForumUserId = forumuserid;
                DiscordUserId = userid;
                GuildId = guildid;
            }

            public override bool Equals(object obj) {
                return obj is ValidationKey vk && Equals(vk);
            }

            protected bool Equals(ValidationKey other) {
                return string.Equals(Key, other.Key) && KeyScope == other.KeyScope && ForumUserId == other.ForumUserId && DiscordUserId == other.DiscordUserId && GuildId == other.GuildId;
            }

            public override int GetHashCode() {
                unchecked {
                    var hashCode = (Key != null ? Key.GetHashCode() : 0);
                    hashCode = (hashCode * 397) ^ (int) KeyScope;
                    hashCode = (hashCode * 397) ^ ForumUserId.GetHashCode();
                    hashCode = (hashCode * 397) ^ DiscordUserId.GetHashCode();
                    hashCode = (hashCode * 397) ^ GuildId.GetHashCode();
                    return hashCode;
                }
            }
        }

        public enum KeyScope
        {
            Forum, Discord
        }
    }
}