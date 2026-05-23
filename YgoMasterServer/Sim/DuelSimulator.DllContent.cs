using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;
using System.IO;

namespace YgoMaster
{
    partial class DuelSimulator
    {
        public bool InitContent()
        {
            string cardDataDir = Path.Combine(DataDir, "CardData");
            if (!Directory.Exists(cardDataDir))
            {
                return false;
            }

            byte[] bufferInternalID = File.ReadAllBytes(Path.Combine(cardDataDir, "#", "CARD_IntID.bytes"));
            DLL_SetInternalID(bufferInternalID);

            byte[] bufferProp = File.ReadAllBytes(Path.Combine(cardDataDir, "#", "CARD_Prop.bytes"));
            DLL_SetCardProperty(bufferProp, bufferProp.Length);

            byte[] bufferSame = File.ReadAllBytes(Path.Combine(cardDataDir, "MD", "CARD_Same.bytes"));
            DLL_SetCardSame(bufferSame, bufferSame.Length);
            
            byte[] bufferGenre = File.ReadAllBytes(Path.Combine(cardDataDir, "#", "CARD_Genre.bytes"));
            DLL_SetCardGenre(bufferGenre);
            
            byte[] bufferNamed = File.ReadAllBytes(Path.Combine(cardDataDir, "#", "CARD_Named.bytes"));
            DLL_SetCardNamed(bufferNamed);
            
            byte[] bufferLink = File.ReadAllBytes(Path.Combine(cardDataDir, "MD", "CARD_Link.bytes"));
            DLL_SetCardLink(bufferLink, bufferLink.Length);

            return true;
        }

        // Load card content + allocate engine work memory so the DLL_CardGet* queries are usable
        // without running a duel (deck builder / card inspection).
        public bool InitForCardQueries()
        {
            if (!InitContent()) return false;
            int num = DLL_SetWorkMemory(IntPtr.Zero);
            DLL_SetWorkMemory(Marshal.AllocHGlobal(num));
            return true;
        }

        public int GetAttribute(int cardId) { return DLL_CardGetAttr(cardId); }   // element (1..7)
        public int GetRace(int cardId) { return DLL_CardGetType(cardId); }        // monster type/race
        public int GetFrame(int cardId) { return DLL_CardGetFrame(cardId); }      // CardFrame
        public int GetLevel(int cardId) { return DLL_CardGetLevel(cardId); }
        public int CheckName(int cardId, int nameType) { return DLL_CardCheckName(cardId, nameType); } // CARD_Named (archetype)
        public int GetLinkNum(int cardId) { return DLL_CardGetLinkNum(cardId); }
        public int GetLinkMask(int cardId) { return DLL_CardGetLinkMask(cardId); }

        // Call DLL_CardGetLinkCards directly (NOT gated on GetLinkNum). Fills `buf` and returns the
        // dll's return value: the count of 16-bit card ids (each int packs two: lo16 then hi16).
        public int GetLinkCardsRaw(int cardId, int[] buf)
        {
            GCHandle h = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try { return DLL_CardGetLinkCards(cardId, h.AddrOfPinnedObject()); }
            finally { h.Free(); }
        }

        // The Deck-Editor "Related Cards" of a card (deduped). Unpacks DLL_CardGetLinkCards' buffer
        // of 16-bit ids. This is the game's exact related list (e.g. Blue-Eyes -> Maiden with Eyes
        // of Blue, Sage with Eyes of Blue, Paladin of White Dragon, ...).
        public List<int> GetRelatedCards(int cardId)
        {
            int[] buf = new int[1024];
            int ret = GetLinkCardsRaw(cardId, buf);
            List<int> result = new List<int>();
            HashSet<int> seen = new HashSet<int>();
            for (int j = 0; j < ret && j < buf.Length * 2; j++)
            {
                int v = (j % 2 == 0) ? (buf[j / 2] & 0xFFFF) : ((buf[j / 2] >> 16) & 0xFFFF);
                if (v != 0 && seen.Add(v)) result.Add(v);
            }
            return result;
        }

        [DllImport(dllName)]
        private static extern int DLL_CardCheckName(int cardId, int nameType);
        [DllImport(dllName)]
        private static extern int DLL_CardGetAltCardID(int cardId, int alterID);
        [DllImport(dllName)]
        private static extern int DLL_CardGetAlterID(int cardId);
        [DllImport(dllName)]
        private static extern int DLL_CardGetAtk(int cardId);
        [DllImport(dllName)]
        private static extern int DLL_CardGetAtk2(int cardId);
        [DllImport(dllName)]
        private static extern int DLL_CardGetAttr(int cardId);
        [DllImport(dllName)]
        private static extern int DLL_CardGetBasicVal(int cardId, ref BasicVal pVal);
        [DllImport(dllName)]
        private static extern int DLL_CardGetDef(int cardId);
        [DllImport(dllName)]
        private static extern int DLL_CardGetDef2(int cardId);
        [DllImport(dllName)]
        private static extern int DLL_CardGetFrame(int cardId);
        [DllImport(dllName)]
        private static extern int DLL_CardGetIcon(int cardId);
        [DllImport(dllName)]
        private static extern int DLL_CardGetInternalID(int cardId);
        [DllImport(dllName)]
        private static extern int DLL_CardGetKind(int cardId);
        [DllImport(dllName)]
        private static extern int DLL_CardGetLevel(int cardId);
        [DllImport(dllName)]
        private static extern int DLL_CardGetLimitation(int cardId);
        [DllImport(dllName)]
        private static extern int DLL_CardGetLinkCards(int cardId, IntPtr pLinkID);
        [DllImport(dllName)]
        private static extern int DLL_CardGetLinkMask(int cardId);
        [DllImport(dllName)]
        private static extern int DLL_CardGetLinkNum(int cardId);
        [DllImport(dllName)]
        private static extern int DLL_CardGetOriginalID(int cardId);
        [DllImport(dllName)]
        private static extern int DLL_CardGetOriginalID2(int cardId);
        [DllImport(dllName)]
        private static extern int DLL_CardGetRank(int cardId);
        [DllImport(dllName)]
        private static extern int DLL_CardGetScaleL(int cardId);
        [DllImport(dllName)]
        private static extern int DLL_CardGetScaleR(int cardId);
        [DllImport(dllName)]
        private static extern int DLL_CardGetStar(int cardId);
        [DllImport(dllName)]
        private static extern int DLL_CardGetType(int cardId);
        [DllImport(dllName)]
        private static extern int DLL_CardIsThisCardGenre(int cardId, int genreId);
        [DllImport(dllName)]
        private static extern int DLL_CardIsThisSameCard(int cardA, int cardB);
        [DllImport(dllName)]
        private static extern int DLL_CardIsThisTunerMonster(int cardId);
        [DllImport(dllName)]
        private static extern void DLL_SetCardGenre(byte[] data);
        [DllImport(dllName)]
        private static extern void DLL_SetCardLink(byte[] data, int size);
        [DllImport(dllName)]
        private static extern void DLL_SetCardNamed(byte[] data);
        [DllImport(dllName)]
        private static extern int DLL_SetCardProperty(byte[] data, int size);
        [DllImport(dllName)]
        private static extern void DLL_SetCardSame(byte[] data, int size);
        [DllImport(dllName)]
        private static extern void DLL_SetInternalID(byte[] data);
    }
}
