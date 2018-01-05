﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Mitternacht.Services.Database.Models;

namespace Mitternacht.Services.Database.Repositories.Impl
{
    public class NicknameHistoryRepository : Repository<NicknameHistoryModel>, INicknameHistoryRepository
    {
        public NicknameHistoryRepository(DbContext context) : base(context)
        {
        }

        public IEnumerable<NicknameHistoryModel> GetGuildUserNames(ulong guildId, ulong userId)
            => _set.Where(n => n.GuildId == guildId && n.UserId == userId).OrderByDescending(n => n.DateAdded);

        public IEnumerable<NicknameHistoryModel> GetUserNames(ulong userId)
            => _set.Where(n => n.UserId == userId);

        public bool AddUsername(ulong guildId, ulong userId, string nickname)
        {
            if (string.IsNullOrWhiteSpace(nickname)) return false;

            var current = _set.Where(u => u.UserId == userId).OrderByDescending(u => u.DateSet).FirstOrDefault();
            var now = DateTime.UtcNow;
            if (current != null)
            {
                if (string.Equals(current.Name, nickname, StringComparison.Ordinal))
                {
                    if (!current.DateReplaced.HasValue) return false;
                    current.DateReplaced = null;
                    return true;
                }

                if (!current.DateReplaced.HasValue)
                {
                    current.DateReplaced = now;
                    _set.Update(current);
                }
            }

            _set.Add(new NicknameHistoryModel
            {
                UserId = userId,
                GuildId = guildId,
                Name = nickname,
                DateSet = now
            });
            return true;
        }
    }
}