﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Mitternacht.Common.Attributes;
using Mitternacht.Extensions;
using Mitternacht.Modules.Level.Services;
using Mitternacht.Services;
using Mitternacht.Services.Database.Models;
using Mitternacht.Services.Database.Repositories.Impl;

namespace Mitternacht.Modules.Level
{
    public class Level : NadekoTopLevelModule<LevelService>
    {
        private readonly IBotConfigProvider _bc;
        private readonly DbService _db;
        private readonly IBotCredentials _creds;

        private string CurrencySign => _bc.BotConfig.CurrencySign;

        public Level(IBotConfigProvider bc, IBotCredentials creds, DbService db) {
            _bc = bc;
            _db = db;
            _creds = creds;
        }

        [NadekoCommand, Usage, Description, Aliases]
        [RequireContext(ContextType.Guild)]
        public async Task Rank([Remainder] IUser user = null)
        {
            user = user ?? Context.User;
            await Rank(user.Id);
        }

        [NadekoCommand, Usage, Description, Aliases]
        [RequireContext(ContextType.Guild)]
        public async Task Rank([Remainder] ulong userId = 0)
        {
            userId = userId != 0 ? userId : Context.User.Id;

            LevelModel lm;
            int total;
            int rank;
            using (var uow = _db.UnitOfWork)
            {
                lm = uow.LevelModel.GetOrCreate(userId);
                total = uow.LevelModel.GetAll().Count();
                rank = uow.LevelModel.GetAll().OrderByDescending(p => p.TotalXP).ToList().IndexOf(lm) + 1;
                await uow.CompleteAsync().ConfigureAwait(false);
            }
            
            if (userId == Context.User.Id)
            {
                await Context.Channel.SendMessageAsync($"{ Context.User.Mention }: **LEVEL { lm.Level } | XP { lm.CurrentXP }/{ LevelModelRepository.GetXpToNextLevel(lm.Level) } | TOTAL XP { lm.TotalXP } | RANK { rank }/{ total }**");
            }
            else
            {
                var user = await Context.Guild.GetUserAsync(userId).ConfigureAwait(false);
                await Context.Channel.SendMessageAsync($"{ Context.User.Mention }: **{user?.Nickname ?? (user?.Username ?? userId.ToString())}\'s Rang > LEVEL { lm.Level } | XP { lm.CurrentXP }/{ LevelModelRepository.GetXpToNextLevel(lm.Level) } | TOTAL XP { lm.TotalXP } | RANK { rank }/{ total }**");
            }
        }

        [NadekoCommand, Usage, Description, Aliases]
        public async Task Ranks(int count, [Remainder]int position)
        {
            const int elementsPerList = 20;

            IOrderedEnumerable<LevelModel> levelmodels;
            using (var uow = _db.UnitOfWork)
            {
                levelmodels = uow.LevelModel.GetAll().Where(p => p.TotalXP > 0).OrderByDescending(p => p.TotalXP);
                await uow.CompleteAsync().ConfigureAwait(false);
            }

            if (!levelmodels.Any()) return;
            position--;
            if(position < 0 || position >= levelmodels.Count()) position = 0;
            if (count <= 0 || count > levelmodels.Count() - position) count = levelmodels.Count() - position;

            var rankstrings = new List<string>();
            var sb = new StringBuilder();
            sb.AppendLine("__**Rangliste**__");
            for (var i = position; i < count + position; i++)
            {
                var lm = levelmodels.ElementAt(i);
                var user = await Context.Guild.GetUserAsync(lm.UserId).ConfigureAwait(false);

                if ((i - position) % elementsPerList == 0)
                    sb.AppendLine($"```Liste {Math.Floor((i - position) / 20f) + 1}\nRang | {"Username", -37} | Lvl | {"XP", -13} | Total XP\n-----|---------------------------------------|-----|---------------|---------");
                if (lm.TotalXP > 0) sb.AppendLine($"{i + 1,3}. | {(user?.Username.TrimTo(32, true) ?? lm.UserId.ToString().TrimTo(32,true)) + (user == null ? "" : $"#{user.DiscriminatorValue:D4}"), -37} | {lm.Level,3} | {lm.CurrentXP,6}/{LevelModelRepository.GetXpToNextLevel(lm.Level),6} | {lm.TotalXP,8}");
                if ((i - position) % elementsPerList != elementsPerList - 1) continue;
                sb.Append("```");
                rankstrings.Add(sb.ToString());
                sb.Clear();
            }

            if(sb.Length > 0)
            {
                sb.Append("```");
                rankstrings.Add(sb.ToString());
                sb.Clear();
            }

            var channel = count <= 20 ? Context.Channel : await Context.User.GetOrCreateDMChannelAsync();

            foreach (var s in rankstrings)
            {
                await channel.SendMessageAsync(s);
                Thread.Sleep(250);
            }
        }

        [NadekoCommand, Usage, Description, Aliases]
        public async Task Ranks([Remainder] int count = 20)
        {
            await Ranks(count, 0);
        }

        [NadekoCommand, Usage, Description, Aliases]
        [RequireContext(ContextType.Guild)]
        [OwnerOnly]
        public async Task AddXp(int xp, [Remainder] IUser user = null)
        {
            user = user ?? Context.User;
            using (var uow = _db.UnitOfWork)
            {
                var success = uow.LevelModel.TryAddXp(user.Id, xp, false);
                await Context.Channel.SendConfirmAsync(success ? $"{Context.User.Mention}: {xp}XP an {user.Username} vergeben." : $"{Context.User.Mention}: Vergabe von {xp}XP an {user.Username} nicht möglich!");
                var level = uow.LevelModel.CalculateLevel(user.Id);
                await _service.SendLevelChangedMessage(level, user, Context.Channel);
                await uow.CompleteAsync().ConfigureAwait(false);
            }
        }

        [NadekoCommand, Usage, Description, Aliases]
        [RequireContext(ContextType.Guild)]
        [OwnerOnly]
        public async Task SetXp(int xp, [Remainder] IUser user = null)
        {
            user = user ?? Context.User;
            using (var uow = _db.UnitOfWork)
            {
                uow.LevelModel.SetXp(user.Id, xp, false);
                await Context.Channel.SendConfirmAsync($"{Context.User.Mention}: XP von {user.Username} auf {xp} gesetzt.");
                var level = uow.LevelModel.CalculateLevel(user.Id);
                await _service.SendLevelChangedMessage(level, user, Context.Channel);
                await uow.CompleteAsync().ConfigureAwait(false);
            }
        }

        [NadekoCommand, Usage, Description, Aliases]
        [RequireContext(ContextType.Guild)]
        public async Task TurnToXp(long moneyToSpend, [Remainder] IUser user = null)
        {
            user = user != null && _creds.IsOwner(Context.User) ? user : Context.User;
            if(moneyToSpend < 0)
            {
                await Context.Channel.SendMessageAsync($"Pech gehabt, {Context.User.Mention}, du kannst XP nicht in Geld zurückverwandeln.");
                return;
            }
            if(moneyToSpend == 0)
            {
                await Context.Channel.SendMessageAsync($"{Context.User.Mention}, 0 {CurrencySign} sind 0 XP!");
                return;
            }
            using (var uow = _db.UnitOfWork)
            {
                var cur = uow.Currency.GetUserCurrency(user.Id);
                if(cur < moneyToSpend)
                {
                    await Context.Channel.SendMessageAsync(user == Context.User ? $"Du hast nicht genug Geld, {Context.User.Mention}!" : $"{Context.User.Mention}: {user.Username} hat nicht genügend Geld!");
                }
                else
                {
                    uow.LevelModel.TryAddXp(user.Id, (int)moneyToSpend * 5, false);
                    uow.Currency.TryUpdateState(user.Id, -moneyToSpend);
                    await Context.Channel.SendMessageAsync($"{Context.User.Mention}: {moneyToSpend}{CurrencySign} in {moneyToSpend * 5}XP umgewandelt" + (user != Context.User ? $" für {user.Username}" : ""));
                    var level = uow.LevelModel.CalculateLevel(user.Id);
                    await _service.SendLevelChangedMessage(level, user, Context.Channel);
                }
                await uow.CompleteAsync().ConfigureAwait(false);
            }
        }

        [NadekoCommand, Usage, Description, Aliases]
        [RequireContext(ContextType.Guild)]
        [OwnerOnly]
        public async Task SetRoleLevelBinding(IRole role, int minlevel)
        {
            if (minlevel < 0) return;
            using (var uow = _db.UnitOfWork)
            {
                uow.RoleLevelBinding.SetBinding(role.Id, minlevel);
                await uow.CompleteAsync().ConfigureAwait(false);
            }
            await Context.Channel.SendMessageAsync($"Die Rolle {role.Name} wird nun Nutzern ab Level {minlevel} vergeben.");
        }

        [NadekoCommand, Usage, Description, Aliases]
        [RequireContext(ContextType.Guild)]
        [OwnerOnly]
        public async Task RemoveRoleLevelBinding(IRole role)
        {
            bool wasRemoved;
            using (var uow = _db.UnitOfWork)
            {
                wasRemoved = uow.RoleLevelBinding.Remove(role.Id);
                await uow.CompleteAsync().ConfigureAwait(false);
            }
            await Context.Channel.SendMessageAsync(wasRemoved ? $"Die Rolle {role.Name} ist nun levelunabhängig." : $"Für die Rolle {role.Name} gibt es keine Steigerung der Levelunabhängigkeit!");
        }

        [NadekoCommand, Usage, Description, Aliases]
        [RequireContext(ContextType.Guild)]
        public async Task RoleLevelBindings(int count, [Remainder]int position)
        {
            const int elementsPerList = 20;

            IOrderedEnumerable<RoleLevelBinding> rlbs;
            using (var uow = _db.UnitOfWork)
            {
                rlbs = uow.RoleLevelBinding.GetAll().OrderByDescending(r => r.MinimumLevel);
                await uow.CompleteAsync().ConfigureAwait(false);
            }

            if (!rlbs.Any()) return;
            position--;
            if (position < 0 || position >= rlbs.Count()) position = 0;
            if (count <= 0 || count > rlbs.Count() - position) count = rlbs.Count() - position;

            var rankstrings = new List<string>();
            var sb = new StringBuilder();
            sb.AppendLine("__**Rolle-Level-Beziehungen**__");
            for (var i = position; i < count + position; i++)
            {
                var rlb = rlbs.ElementAt(i);
                var role = Context.Guild.GetRole(rlb.RoleId);

                if ((i - position) % elementsPerList == 0) sb.AppendLine($"```Liste {Math.Floor((i - position) / 20f) + 1}");
                sb.AppendLine($"{i + 1,3}. | {role.Name, -20} | Level {rlb.MinimumLevel}+");
                if ((i - position) % elementsPerList != elementsPerList - 1) continue;
                sb.Append("```");
                rankstrings.Add(sb.ToString());
                sb.Clear();
            }

            if (sb.Length > 0)
            {
                sb.Append("```");
                rankstrings.Add(sb.ToString());
                sb.Clear();
            }

            var channel = count <= 20 ? Context.Channel : await Context.User.GetOrCreateDMChannelAsync();

            foreach (var s in rankstrings)
            {
                await channel.SendMessageAsync(s);
                Thread.Sleep(250);
            }
        }

        [NadekoCommand, Usage, Description, Aliases]
        [RequireContext(ContextType.Guild)]
        public async Task RoleLevelBindings([Remainder]int count = 20) 
            => await RoleLevelBindings(count, 1);

        [NadekoCommand, Usage, Description, Aliases]
        [RequireContext(ContextType.Guild)]
        [OwnerOnly]
        public async Task MsgXpRestrictionAdd(ITextChannel channel) {
            using (var uow = _db.UnitOfWork) {
                var success = await uow.MessageXpBlacklist.CreateRestrictionAsync(channel);
                if (success)
                    await Context.Channel.SendConfirmAsync($"Nachrichten in Kanal {channel.Mention} geben nun keine XP mehr.");
                else
                    await Context.Channel.SendErrorAsync($"In Kanal {channel.Mention} gibt es schon keine XP mehr auf Nachrichten.");
            }
        }

        [NadekoCommand, Usage, Description, Aliases]
        [RequireContext(ContextType.Guild)]
        [OwnerOnly]
        public async Task MsgXpRestrictionRemove(ITextChannel channel) {
            using (var uow = _db.UnitOfWork) {
                var success = await uow.MessageXpBlacklist.RemoveRestrictionAsync(channel);
                if (success)
                    await Context.Channel.SendConfirmAsync($"Nachrichten in Channel {channel.Mention} geben nun wieder XP.");
                else
                    await Context.Channel.SendErrorAsync($"In Channel {channel.Mention} gibt es schon XP auf Nachrichten.");
            }
        }

        [NadekoCommand, Usage, Description, Aliases]
        [RequireContext(ContextType.Guild)]
        public async Task MsgXpRestrictions() {
            using (var uow = _db.UnitOfWork) {
                if (!uow.MessageXpBlacklist.GetAll().Any()) await Context.Channel.SendErrorAsync("Es gibt keine Kanäle, in denen es keine XP auf Nachrichten gibt.");
                else
                    await Context.Channel.SendConfirmAsync("Kanäle ohne Nachrichten-XP", uow.MessageXpBlacklist.GetAll().OrderByDescending(m => m.ChannelId).Aggregate("", (s, m) => $"{s}<#{m.ChannelId}>, ", s => s.Substring(0, s.Length - 2)));
            }
        }
    }
}
