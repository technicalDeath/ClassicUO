using System.Collections.Generic;
using ClassicUO.Game.UI.Gumps;

namespace ClassicUO.Game.Managers
{
    /// <summary>
    /// The shard's spell chances (extended command 0xAF): for each spell, by the client's spell number, the chance one cast raises the cast skill and
    /// the chance it succeeds. The spellbook and the spell icons add them to their tooltips; an old shard never sends them and nothing changes.
    /// </summary>
    internal static class SpellChances
    {
        public const int CommandId = 0xAF;

        /// <summary>The byte for a spell the shard has no numbers for.</summary>
        public const byte None = 255;

        private static readonly Dictionary<int, (byte Gain, byte Cast)> _chances = new Dictionary<int, (byte, byte)>();

        public static string Describe(int spellId, string text)
        {
            if (string.IsNullOrEmpty(text) || !_chances.TryGetValue(spellId, out var c))
            {
                return text;
            }

            return $"{text}\nChance to raise Magery: {c.Gain}%\nChance to cast it: {c.Cast}%";
        }

        public static void OnCommand(ref ClassicUO.IO.StackDataReader p)
        {
            int count = p.ReadUInt16BE();

            for (int i = 0; i < count && p.Remaining >= 4; i++)
            {
                int spell = p.ReadUInt16BE();
                byte gain = p.ReadUInt8();
                byte cast = p.ReadUInt8();

                if (gain == None)
                {
                    _chances.Remove(spell);
                }
                else
                {
                    _chances[spell] = (gain, cast);
                }
            }

            foreach (Gump g in UIManager.Gumps)
            {
                if (g is SpellbookGump book)
                {
                    book.RefreshChanceTooltips();
                }
                else if (g is UseSpellButtonGump button)
                {
                    button.RefreshChanceTooltip();
                }
            }
        }
    }
}
