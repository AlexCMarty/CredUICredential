using System;
using System.Runtime.InteropServices;
using CredUICredential.Pinvoke;
using Xunit;

namespace CredUICredential.Tests
{
    /// <summary>
    ///     Local-admin membership is read off <c>TokenGroups</c>, including the deny-only slot
    ///     UAC uses on a filtered network token. <c>CheckTokenMembership</c> would miss that.
    /// </summary>
    public class TokenGroupsTests : IDisposable
    {
        private readonly IntPtr _administrators = WellKnownSid(ADVAPI.WinBuiltinAdministratorsSid);
        private readonly IntPtr _everyone = WellKnownSid(ADVAPI.WinWorldSid);

        public void Dispose()
        {
            Marshal.FreeHGlobal(_administrators);
            Marshal.FreeHGlobal(_everyone);
        }

        [Fact]
        public void AnEnabledAdministratorsSidCounts()
        {
            using var groups = TokenGroupsBuffer.Alloc((_administrators, SE_GROUP_ENABLED));

            Assert.True(TokenGroups.ContainsBuiltinAdministrators(groups.DangerousGetHandle()));
        }

        [Fact]
        public void ADenyOnlyAdministratorsSidStillCounts()
        {
            using var groups = TokenGroupsBuffer.Alloc((_administrators, ADVAPI.SE_GROUP_USE_FOR_DENY_ONLY));

            Assert.True(TokenGroups.ContainsBuiltinAdministrators(groups.DangerousGetHandle()));
        }

        [Fact]
        public void ATokenWithoutAdministratorsIsNotAdmin()
        {
            using var groups = TokenGroupsBuffer.Alloc((_everyone, SE_GROUP_ENABLED));

            Assert.False(TokenGroups.ContainsBuiltinAdministrators(groups.DangerousGetHandle()));
        }

        [Fact]
        public void AnEmptyGroupListIsNotAdmin()
        {
            using var groups = TokenGroupsBuffer.Alloc(Array.Empty<(IntPtr, uint)>());

            Assert.False(TokenGroups.ContainsBuiltinAdministrators(groups.DangerousGetHandle()));
        }

        private const uint SE_GROUP_ENABLED = 0x00000004;

        private static IntPtr WellKnownSid(int type)
        {
            var size = 0;
            ADVAPI.CreateWellKnownSid(type, IntPtr.Zero, IntPtr.Zero, ref size);
            var sid = Marshal.AllocHGlobal(size);
            if (!ADVAPI.CreateWellKnownSid(type, IntPtr.Zero, sid, ref size))
            {
                Marshal.FreeHGlobal(sid);
                throw new System.ComponentModel.Win32Exception();
            }

            return sid;
        }
    }

    /// <summary>
    ///     A <c>TOKEN_GROUPS</c> blob allocated on the native heap, with SID pointers the
    ///     production reader can walk the same way <c>GetTokenInformation</c> would lay them out.
    /// </summary>
    internal sealed class TokenGroupsBuffer : IDisposable
    {
        private readonly IntPtr _handle;

        private TokenGroupsBuffer(IntPtr handle) => _handle = handle;

        public IntPtr DangerousGetHandle() => _handle;

        public static TokenGroupsBuffer Alloc(params (IntPtr Sid, uint Attributes)[] groups)
        {
            var entrySize = Marshal.SizeOf<ADVAPI.SID_AND_ATTRIBUTES>();
            var header = Marshal.OffsetOf<ADVAPI.TOKEN_GROUPS>(nameof(ADVAPI.TOKEN_GROUPS.Groups)).ToInt32();
            var size = header + (entrySize * Math.Max(groups.Length, 1));
            var buffer = Marshal.AllocHGlobal(size);
            for (var offset = 0; offset < size; offset++)
            {
                Marshal.WriteByte(buffer, offset, 0);
            }

            Marshal.WriteInt32(buffer, groups.Length);
            for (var i = 0; i < groups.Length; i++)
            {
                var entry = new ADVAPI.SID_AND_ATTRIBUTES
                {
                    Sid = groups[i].Sid,
                    Attributes = groups[i].Attributes
                };
                Marshal.StructureToPtr(entry, buffer + header + (i * entrySize), fDeleteOld: false);
            }

            return new TokenGroupsBuffer(buffer);
        }

        public void Dispose() => Marshal.FreeHGlobal(_handle);
    }
}
