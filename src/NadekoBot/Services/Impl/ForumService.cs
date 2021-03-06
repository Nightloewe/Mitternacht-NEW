﻿using System.Threading.Tasks;
using GommeHDnetForumAPI;
using NLog;

namespace Mitternacht.Services.Impl
{
    public class ForumService : INService
    {
        private readonly IBotCredentials _creds;
        private readonly Logger _log;
        
        public Forum Forum { get; private set; }
        public bool LoggedIn => Forum?.LoggedIn ?? false;
        private Task _loginTask;

        public ForumService(IBotCredentials creds) {
            _creds = creds;
            _log = LogManager.GetCurrentClassLogger();
            InitForumInstance();
        }

        public void InitForumInstance()
        {
            _loginTask?.Dispose();
            _loginTask = Task.Run(() => {
                Forum = new Forum(_creds.ForumUsername, _creds.ForumPassword);
                _log.Log(Forum.LoggedIn ? LogLevel.Info : LogLevel.Warn, $"Initialized new Forum instance. Login {(Forum.LoggedIn ? "successful" : "failed, ignoring verification actions")}!");
            });
        }
    }
}