﻿using CK.DB.Auth;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CK.SqlServer;
using System.Threading;
using CK.Core;

namespace CK.DB.AspNet.Auth.Tests
{
    public class MockAuthBasicProvider : IBasicAuthenticationProvider
    {
        readonly MockAuthDatabaseService _db;

        public MockAuthBasicProvider( MockAuthDatabaseService db )
        {
            _db = db;
        }

        public CreateOrUpdateResult CreateOrUpdatePasswordUser(ISqlCallContext ctx, int actorId, int userId, string password, CreateOrUpdateMode mode = CreateOrUpdateMode.CreateOrUpdate)
        {
            return _db.CreateOrUpdateUser(userId, mode, "Basic" );
        }

        public Task<CreateOrUpdateResult> CreateOrUpdatePasswordUserAsync(ISqlCallContext ctx, int actorId, int userId, string password, CreateOrUpdateMode mode = CreateOrUpdateMode.CreateOrUpdate, CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.FromResult(CreateOrUpdatePasswordUser(ctx,actorId,userId,password,mode));
        }

        public void DestroyPasswordUser(ISqlCallContext ctx, int actorId, int userId)
        {
            _db.DestroyUser(userId, "Basic");
        }

        public Task DestroyPasswordUserAsync(ISqlCallContext ctx, int actorId, int userId, CancellationToken cancellationToken = default(CancellationToken))
        {
            DestroyPasswordUser(ctx, actorId, userId);
            return Task.FromResult(0);
        }

        public int LoginUser(ISqlCallContext ctx, string userName, string password, bool actualLogin = true)
        {
            return _db.LoginUser(userName, password, actualLogin, "Basic");
        }

        public int LoginUser(ISqlCallContext ctx, int userId, string password, bool actualLogin = true)
        {
            return _db.LoginUser(userId, password, actualLogin, "Basic");
        }

        public Task<int> LoginUserAsync(ISqlCallContext ctx, string userName, string password, bool actualLogin = true, CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.FromResult(LoginUser(ctx, userName, password, actualLogin));
        }

        public Task<int> LoginUserAsync(ISqlCallContext ctx, int userId, string password, bool actualLogin = true, CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.FromResult(LoginUser(ctx, userId, password, actualLogin));
        }

        public void SetPassword(ISqlCallContext ctx, int actorId, int userId, string password)
        {
        }

        public Task SetPasswordAsync(ISqlCallContext ctx, int actorId, int userId, string password, CancellationToken cancellationToken = default(CancellationToken))
        {
            return Task.FromResult(0);
        }
    }
}