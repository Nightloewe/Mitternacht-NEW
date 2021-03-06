﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Mitternacht.Extensions;
using Mitternacht.Services;
using Mitternacht.Services.Database.Repositories;
using Mitternacht.Services.Database.Repositories.Impl;

namespace Mitternacht.Modules.Level.Services
{
    public class LevelService : INService
    {
        private readonly DiscordSocketClient _client;
        private readonly DbService _db;
        private readonly CommandHandler _ch;

        public LevelService(DiscordSocketClient client, DbService db, CommandHandler ch)
        {
            _client = client;
            _db = db;
            _ch = ch;
            _ch.OnMessageNoTrigger += OnMessageNoTrigger;
            //client.MessageUpdated += OnMessageUpdated;
            client.MessageDeleted += OnMessageDeleted;
            client.MessageReceived += AddLevelRole;
            LevelModelRepository.LevelChanged += SendLevelChangedMessage;
        }

        private async Task AddLevelRole(SocketMessage sm)
        {
            if (!(sm.Author is IGuildUser user)) return;

            List<IRole> rolesToAdd;
            using (var uow = _db.UnitOfWork)
            {
                var level = uow.LevelModel.GetLevel(user.GuildId, user.Id);
                var userroles = user.GetRoles().ToList();
                var rlb = uow.RoleLevelBinding.GetAll()
                    .Where(rl => rl.MinimumLevel <= level && userroles.All(ur => ur.Id != rl.RoleId)).ToList();
                rolesToAdd = user.Guild.Roles.Where(r => rlb.Any(rs => rs.RoleId == r.Id)).ToList();
            }

            if (!rolesToAdd.Any()) return;
            var rolestring =
                rolesToAdd.Aggregate("\"", (s, r) => $"{s}{r.Name}\", \"", s => s.Substring(0, s.Length - 3));
            await user.AddRolesAsync(rolesToAdd).ConfigureAwait(false);
            await sm.Channel
                .SendMessageAsync(
                    $"{user.Mention} hat die Rolle{(rolesToAdd.Count > 1 ? "n" : "")} {rolestring} bekommen.")
                .ConfigureAwait(false);
        }

        private async Task OnMessageNoTrigger(IUserMessage um)
        {
            if (!(um.Author is IGuildUser user)) return;
            using (var uow = _db.UnitOfWork)
            {
                if (uow.MessageXpBlacklist.IsRestricted(um.Channel as ITextChannel)
                    || um.Content.Length < uow.GuildConfigs.For(user.GuildId, set => set).MessageXpCharCountMin)
                    return;

                var time = DateTime.Now;
                if (uow.LevelModel.CanGetMessageXp(user.GuildId, user.Id, time))
                {
                    var maxXp = uow.GuildConfigs.For(user.GuildId, set => set).MessageXpCharCountMax;
                    uow.LevelModel.AddXp(user.GuildId, user.Id, um.Content.Length > maxXp ? maxXp : um.Content.Length,
                        um.Channel.Id);
                    uow.LevelModel.ReplaceTimestamp(user.GuildId, user.Id, time);
                }

                await uow.CompleteAsync().ConfigureAwait(false);
            }
        }

        //private async Task OnMessageUpdated(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel channel)
        //{
        //    var msgBefore = await before.GetOrDownloadAsync();
        //    if (!(msgBefore.Author is IGuildUser user)) return;

        //    var execBefore = await _ch.WouldGetExecuted(msgBefore);
        //    var execAfter = await _ch.WouldGetExecuted(after);

        //    if (execBefore && execAfter || msgBefore.Content.Length == after.Content.Length) return;

        //    using (var uow = _db.UnitOfWork) {
        //        if (await uow.MessageXpBlacklist.IsRestrictedAsync(channel as ITextChannel)) return;
        //        uow.LevelModel.AddXp(user.GuildId, user.Id, after.Content.Length - msgBefore.Content.Length, channel.Id); //todo: fix XP leak
        //        await uow.CompleteAsync().ConfigureAwait(false);
        //    }
        //}

        private async Task OnMessageDeleted(Cacheable<IMessage, ulong> before, ISocketMessageChannel channel)
        {
            var msg = await before.GetOrDownloadAsync();
            if (!(msg.Author is IGuildUser user) || await _ch.WouldGetExecuted(msg)) return;

            using (var uow = _db.UnitOfWork)
            {
                if (uow.MessageXpBlacklist.IsRestricted(channel as ITextChannel)) return;
                uow.LevelModel.AddXp(user.GuildId, user.Id,
                    -uow.GuildConfigs.For(user.GuildId, set => set).MessageXpCharCountMax, channel.Id);
                await uow.CompleteAsync().ConfigureAwait(false);
            }
        }

        private async Task SendLevelChangedMessage(LevelChangedArgs lc)
        {
            if (lc.ChannelId == null) return;
            var channel = _client.GetGuild(lc.GuildId)?.GetTextChannel(lc.ChannelId.Value);
            if (channel == null) return;

            if (lc.ChangeType == LevelChangedArgs.ChangeTypes.Up)
                await channel
                    .SendConfirmAsync(
                        $"Herzlichen Glückwunsch {MentionUtils.MentionUser(lc.UserId)}, du bist von Level {lc.OldLevel} auf Level {lc.NewLevel} aufgestiegen!")
                    .ConfigureAwait(false);
            else if (lc.ChangeType == LevelChangedArgs.ChangeTypes.Down)
                await channel
                    .SendConfirmAsync(
                        $"Schade {MentionUtils.MentionUser(lc.UserId)}, du bist von Level {lc.OldLevel} auf Level {lc.NewLevel} abgestiegen.")
                    .ConfigureAwait(false);
        }
    }
}