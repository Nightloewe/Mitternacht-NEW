﻿using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Mitternacht.Services.Database.Models;

namespace Mitternacht.Services.Database.Repositories.Impl
{
    public class UsernameHistoryRepository : Repository<UsernameHistoryModel>, IUsernameHistoryRepository
    {
        public UsernameHistoryRepository(DbContext context) : base(context) { }

        public bool AddUsername(ulong userId, string username) {
            if (string.IsNullOrWhiteSpace(username)) return false;

            var current = _set.Where(u => u.UserId == userId).OrderByDescending(u => u.DateSet).FirstOrDefault();
            var now = DateTime.UtcNow;
            if (current != null)
            {
                if (string.Equals(current.Name, username, StringComparison.Ordinal))
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

            _set.Add(new UsernameHistoryModel {
                UserId = userId,
                Name = username,
                DateSet = now
            });
            return true;
        }

        public IEnumerable<UsernameHistoryModel> GetUserNames(ulong userId)
            => _set.Where(u => u.UserId == userId).OrderByDescending(u => u.DateSet);
    }
}