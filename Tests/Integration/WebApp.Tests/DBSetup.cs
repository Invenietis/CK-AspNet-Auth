using CK.Core;
using CK.DB.Actor;
using CK.DB.Auth;
using CK.DB.User.UserGoogle;
using CK.DB.User.UserOidc;
using CK.DB.User.UserPassword;
using CK.SqlServer;
using CK.SqlServer.Setup;
using NUnit.Framework;
using System;
using System.Threading.Tasks;
using static CK.Testing.DBSetupTestHelper;

namespace WebApp.Tests
{
    [TestFixture]
    public class DBSetup : CK.DB.Tests.DBSetup
    {

        [Test]
        public void Bob_is_totally_unknown()
        {
            BobSetup();
        }

        [Explicit]
        [Test]
        public async Task close_WebApp_and_IdServer_servers()
        {
            var c = await WebAppHelper.GetRunningTestClient();
            WebAppHelper.WebAppProcess.StopAndWaitForExit();
            WebAppHelper.IdServerProcess.StopAndWaitForExit();
        }

        // This unit test is called by Build script (thanks to its category) before
        // building the WebApp.
        [Explicit]
        [Category( "GenerateStObjAssembly" )]
        [Test]
        public void Generate_StObj_Assembly_Generated()
        {
            TestHelper.RunDBSetup();
        }



        public static void BobSetup()
        {
            var oidc = TestHelper.StObjMap.Default.Obtain<UserOidcTable>();
            var user = TestHelper.StObjMap.Default.Obtain<UserTable>();
            using( var ctx = new SqlStandardCallContext() )
            {
                var bob = oidc.FindKnownUserInfo( ctx, "", "Bob_is_totally_unknown" );
                if( bob != null ) user.DestroyUser( ctx, 1, bob.UserId );
            }
        }

        [Test]
        public void Alice_has_only_basic_authentication()
        {
            AliceSetup();
        }

        public static void AliceSetup()
        {
            var oidc = TestHelper.StObjMap.Default.Obtain<UserOidcTable>();
            var google = TestHelper.StObjMap.Default.Obtain<UserGoogleTable>();
            var user = TestHelper.StObjMap.Default.Obtain<UserTable>();
            var userPwd = TestHelper.StObjMap.Default.Obtain<UserPasswordTable>();
            using( var ctx = new SqlStandardCallContext() )
            {
                int id = user.FindByName( ctx, "alice" );
                if( id == 0 )
                {
                    id = user.CreateUser( ctx, 1, "alice" );
                }
                else
                {
                    oidc.DestroyOidcUser( ctx, 1, id, schemeSuffix: "" );
                    google.DestroyGoogleUser( ctx, 1, id );
                }
                userPwd.CreateOrUpdatePasswordUser( ctx, 1, id, "password" );
            }
        }

        [Test]
        public void Carol_is_Basic_and_Oidc_registered()
        {
            CarolSetup();
        }

        public static void CarolSetup()
        {
            var oidc = TestHelper.StObjMap.Default.Obtain<UserOidcTable>();
            var google = TestHelper.StObjMap.Default.Obtain<UserGoogleTable>();
            var user = TestHelper.StObjMap.Default.Obtain<UserTable>();
            var userPwd = TestHelper.StObjMap.Default.Obtain<UserPasswordTable>();
            using( var ctx = new SqlStandardCallContext() )
            {
                int id = user.FindByName( ctx, "carol" );
                if( id == 0 )
                {
                    id = user.CreateUser( ctx, 1, "carol" );
                }
                else
                {
                    google.DestroyGoogleUser( ctx, 1, id );
                }
                userPwd.CreateOrUpdatePasswordUser( ctx, 1, id, "password", UCLMode.CreateOrUpdate| UCLMode.WithActualLogin );
                var info = oidc.CreateUserInfo<IUserOidcInfo>();
                info.SchemeSuffix = "";
                info.Sub = "Carol_is_Basic_and_Oidc_registered";
                oidc.CreateOrUpdateOidcUser( ctx, 1, id, info );
            }

        }

        public static IDisposable TemporaryDisableAllLogin()
        {
            var db = TestHelper.StObjMap.Default.Obtain<SqlDefaultDatabase>();
            return db.TemporaryTransform( @"
                            create transformer on CK.sAuthUserOnLogin
                            as
                            begin
                                inject ""set @FailureCode = 6; -- GloballyDisabledUser"" into ""CheckLoginFailure"";
                            end
                        " );

        }

    }
}