// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.IO;
using ClassicUO.Network;

namespace ClassicUO.Game.Data
{
    /// <summary>
    /// The shard's carry limit (UO Rekindled, CarryWeight on the server). The era's status packet (version 3) carries no limit, so the
    /// client works out the stock one itself; at login the shard sends extended command 0xB3 with its base and the stones per point of
    /// Strength in tenths, and the limit is worked out from those instead. Until it arrives, and on a server that never sends it, the
    /// stock sum stands. The login copy can arrive before the game scene is up, so the game scene also asks once it is.
    /// </summary>
    internal static class CarryWeight
    {
        public const int CommandId = 0xB3;

        private static int _base = -1;
        private static int _perStrengthTenths;

        public static void Reset() => _base = -1;

        public static void Request()
        {
            if (_base < 0 && NetClient.Socket.IsConnected)
            {
                NetClient.Socket.Send_CarryWeightRequest();
            }
        }

        /// <summary>The shard's limit at this Strength, or null when the shard has not said.</summary>
        public static ushort? LimitFor(ushort strength) =>
            _base < 0 ? null : (ushort)(_base + _perStrengthTenths * strength / 10);

        public static void OnCommand(World world, ref StackDataReader p)
        {
            if (p.Remaining < 4)
            {
                return;
            }

            _base = p.ReadUInt16BE();
            _perStrengthTenths = p.ReadUInt16BE();

            if (world.Player != null && LimitFor(world.Player.Strength) is { } limit)
            {
                world.Player.WeightMax = limit;
            }
        }
    }
}
