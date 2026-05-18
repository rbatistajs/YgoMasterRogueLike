// Goat: renderiza UMA imagem de carta centralizada no ícone do
// chapter quando o chapter dict tem o field `card_image` (= cardId).
//
// É a alternativa ao p1_img/p2_img (atlas chars.dfymoo) que o
// `SoloVisualNovelChapterView` original usa. Aquele depende do atlas
// pre-empacotado de bosses; aqui pedimos pro ResourceManager do jogo
// carregar o asset `Card/Images/Illust/tcg/<cardId>` — ele primeiro
// procura o PNG custom em `<ClientData>/Card/Images/Illust/tcg/<id>.png`,
// e se não existir cai no bundle original do Master Duel (que tem a
// imagem de toda carta do jogo). Sem regerar atlas, sem dependência
// de cache local.
//
// Convivência com o sistema antigo:
//   - Se `card_image` existe → nós renderizamos
//   - Se só `p1_img`/`p2_img` existem → original renderiza
//   - Se ambos existem → ambos rodam (raro; chapter ficaria com 3
//     figuras visíveis, então o lado server evita setar os dois)
//
// Wired via callout em SoloVisualNovelChapterView.OnCreatedView (mesmo
// padrão que SoloGateScrollEnabler / SoloGateGridLayout — one Hook
// per method limit, todos piggyback ali).

using IL2CPP;
using System;
using System.Collections.Generic;
using UnityEngine;
using YgoMaster;

namespace YgoMasterClient
{
    static unsafe class SoloChapterCardImage
    {
        // ----- IL2 reflection cache -----
        static IL2Field SoloSelectChapterViewController_chapterMap;
        static IL2Field ChapterMap_chapterDataDic;
        static IL2Field ChapterMap_gateID;
        static IL2Class Chapter_Class;
        static IL2Field Chapter_go;

        static IntPtr Image_Type;
        static IL2Method Image_SetSprite;
        static IL2Property Image_preserveAspect;
        static IntPtr RectMask2D_Type;
        static IL2Property RectMask2D_padding;
        static IL2Property RectMask2D_softness;
        static IntPtr RectTransform_Type;
        static IL2Property RectTransform_anchoredPosition3D;
        static IL2Property RectTransform_anchorMin;
        static IL2Property RectTransform_anchorMax;
        static IL2Property RectTransform_offsetMin;
        static IL2Property RectTransform_offsetMax;
        static IL2Property RectTransform_pivot;
        static IL2Property RectTransform_sizeDelta;
        static IntPtr CanvasGroup_Type;

        // Sprite cache: cardId -> sprite IntPtr. Sprites passam por
        // gchandle_new após criação pra Unity não destruir.
        static readonly Dictionary<int, IntPtr> spriteCache = new Dictionary<int, IntPtr>();
        static IntPtr cardImagesDontDestroy;

        static SoloChapterCardImage()
        {
            IL2Assembly uiAssembly = Assembler.GetAssembly("UnityEngine.UI");
            IL2Class Image_Class = uiAssembly.GetClass("Image", "UnityEngine.UI");
            Image_Type = Image_Class.IL2Typeof();
            Image_SetSprite = Image_Class.GetProperty("sprite").GetSetMethod();
            Image_preserveAspect = Image_Class.GetProperty("preserveAspect");
            IL2Class RectMask2D_Class = uiAssembly.GetClass("RectMask2D", "UnityEngine.UI");
            RectMask2D_Type = RectMask2D_Class.IL2Typeof();
            RectMask2D_padding = RectMask2D_Class.GetProperty("padding");
            RectMask2D_softness = RectMask2D_Class.GetProperty("softness");

            IL2Assembly coreAssembly = Assembler.GetAssembly("UnityEngine.CoreModule");
            IL2Class RectTransform_Class = coreAssembly.GetClass("RectTransform", "UnityEngine");
            RectTransform_Type = RectTransform_Class.IL2Typeof();
            RectTransform_anchoredPosition3D = RectTransform_Class.GetProperty("anchoredPosition3D");
            RectTransform_anchorMin = RectTransform_Class.GetProperty("anchorMin");
            RectTransform_anchorMax = RectTransform_Class.GetProperty("anchorMax");
            RectTransform_offsetMin = RectTransform_Class.GetProperty("offsetMin");
            RectTransform_offsetMax = RectTransform_Class.GetProperty("offsetMax");
            RectTransform_pivot = RectTransform_Class.GetProperty("pivot");
            RectTransform_sizeDelta = RectTransform_Class.GetProperty("sizeDelta");

            IL2Assembly uiModule = Assembler.GetAssembly("UnityEngine.UIModule");
            IL2Class CanvasGroup_Class = uiModule.GetClass("CanvasGroup", "UnityEngine");
            CanvasGroup_Type = CanvasGroup_Class.IL2Typeof();

            IL2Assembly assembly = Assembler.GetAssembly("Assembly-CSharp");
            IL2Class controllerClass = assembly.GetClass("SoloSelectChapterViewController", "YgomGame.Solo");
            SoloSelectChapterViewController_chapterMap = controllerClass.GetField("chapterMap");
            IL2Class ChapterMap_Class = controllerClass.GetNestedType("ChapterMap");
            ChapterMap_chapterDataDic = ChapterMap_Class.GetField("chapterDataDic");
            ChapterMap_gateID = ChapterMap_Class.GetField("gateID");
            Chapter_Class = controllerClass.GetNestedType("Chapter");
            Chapter_go = Chapter_Class.GetField("go");
        }

        // Piggyback target — chamado pelo SoloVisualNovelChapterView.OnCreatedView.
        public static void OnCreatedView(IntPtr thisPtr)
        {
            try { Apply(thisPtr); }
            catch (Exception ex) { Console.WriteLine("[SoloChapterCardImage] " + ex); }
        }

        static void Apply(IntPtr thisPtr)
        {
            IntPtr chapterMap = SoloSelectChapterViewController_chapterMap.GetValue(thisPtr).ptr;
            int gateId = ChapterMap_gateID.GetValue(chapterMap).GetValueRef<int>();

            Dictionary<string, object> chaptersData =
                YgomSystem.Utility.ClientWork.GetDict("$.Master.Solo.chapter." + gateId);
            if (chaptersData == null) return;

            IL2DictionaryExplicit chapterDataDict = new IL2DictionaryExplicit(
                ChapterMap_chapterDataDic.GetValue(chapterMap).ptr,
                IL2SystemClass.Int32, Chapter_Class);

            foreach (KeyValuePair<string, object> chapter in chaptersData)
            {
                Dictionary<string, object> chapterData = chapter.Value as Dictionary<string, object>;
                if (chapterData == null) continue;

                int chapterId;
                if (!int.TryParse(chapter.Key, out chapterId)) continue;
                if (!chapterDataDict.ContainsKey(chapterId)) continue;

                int cardId = ReadCardImage(chapterData);
                if (cardId <= 0) continue;

                IntPtr sprite = LoadCardSprite(cardId);
                if (sprite == IntPtr.Zero) continue;

                IL2Object goObj = Chapter_go.GetValue(chapterDataDict[chapterId]);
                if (goObj == null) continue;
                IntPtr go = goObj.ptr;
                IntPtr btnObj = GameObject.FindGameObjectByName(go, "Button", false, false);
                if (btnObj == IntPtr.Zero) continue;

                AttachCardImage(btnObj, sprite, chapterId);
            }
        }

        // Aceita int ("card_image": 4007), long, double e string ("4007").
        static int ReadCardImage(Dictionary<string, object> chapterData)
        {
            object v;
            if (!chapterData.TryGetValue("card_image", out v) || v == null) return 0;
            try { return Convert.ToInt32(v); } catch { return 0; }
        }

        // Carrega a card image via ResourceManager do jogo. Caminho
        // "Card/Images/Illust/tcg/<id>" passa pelo hook do AssetHelper:
        // se existe PNG custom em ClientData ele usa, senão cai no
        // bundle nativo do Master Duel — que tem TODAS as cartas.
        //
        // O asset retornado é uma Texture2D (cards não vêm como sprite),
        // que convertemos via SpriteFromTexture pra usar em UI Image.
        static IntPtr LoadCardSprite(int cardId)
        {
            IntPtr existing;
            if (spriteCache.TryGetValue(cardId, out existing)) return existing;

            string path = "Card/Images/Illust/tcg/" + cardId;
            IntPtr texture = AssetHelper.LoadImmediateAsset(path);
            if (texture == IntPtr.Zero)
            {
                Console.WriteLine("[SoloChapterCardImage] no texture for " + path);
                spriteCache[cardId] = IntPtr.Zero;
                return IntPtr.Zero;
            }

            IntPtr sprite = AssetHelper.SpriteFromTexture(texture, "goat_card_" + cardId);
            if (sprite == IntPtr.Zero)
            {
                Console.WriteLine("[SoloChapterCardImage] sprite-from-texture failed for cid " + cardId);
                spriteCache[cardId] = IntPtr.Zero;
                return IntPtr.Zero;
            }

            // Mantém vivo (Unity destruiria entre cenas senão). O texture
            // é gerenciado pelo ResourceManager do jogo; só o sprite
            // novo precisamos ancorar.
            Import.Handler.il2cpp_gchandle_new(sprite, true);
            UnityObject.DontDestroyOnLoad(sprite);
            if (cardImagesDontDestroy == IntPtr.Zero)
            {
                cardImagesDontDestroy = GameObject.New();
                UnityObject.SetName(cardImagesDontDestroy, "GoatCardImagesDontDestroy");
                UnityObject.DontDestroyOnLoad(cardImagesDontDestroy);
            }
            IntPtr keepAlive = GameObject.New();
            UnityObject.SetName(keepAlive, "goat_card_" + cardId);
            IntPtr keepImg = GameObject.AddComponent(keepAlive, Image_Type);
            Image_SetSprite.Invoke(keepImg, new IntPtr[] { sprite });
            Transform.SetParent(GameObject.GetTransform(keepAlive),
                                GameObject.GetTransform(cardImagesDontDestroy));

            spriteCache[cardId] = sprite;
            return sprite;
        }

        // Cria 2 GameObjects no Button do chapter:
        //   GoatCardImage       — container com RectMask2D (clipa ao box)
        //     └─ Img            — Image+sprite com preserveAspect=true
        //                         (mantém proporção, sem distorção)
        // Esconde o "Icon" original se existir.
        static void AttachCardImage(IntPtr btnObj, IntPtr sprite, int chapterId)
        {
            // Idempotente: se já criamos pra esse botão (cena reusada),
            // não duplicar.
            IntPtr existing = GameObject.FindGameObjectByName(btnObj, "GoatCardImage", false, false);
            if (existing != IntPtr.Zero) return;

            IntPtr iconObj = GameObject.FindGameObjectByName(btnObj, "Icon", false, false);
            if (iconObj != IntPtr.Zero) GameObject.SetActive(iconObj, false);

            // Container com RectMask2D — clipa filhos ao retângulo dele.
            // Ordem importante:
            //   1) AddComponent(RectTransform) substitui o Transform
            //      padrão de GameObject.New().
            //   2) GetTransform() retorna o RectTransform novo.
            //   3) SetParent + setters UI.
            // Invertido = NRE em set_anchorMin etc.
            IntPtr containerObj = GameObject.New();
            UnityObject.SetName(containerObj, "GoatCardImage");
            GameObject.AddComponent(containerObj, CanvasGroup_Type);
            GameObject.AddComponent(containerObj, RectTransform_Type);
            IntPtr maskComp = GameObject.AddComponent(containerObj, RectMask2D_Type);
            // Padding (left, bottom, right, top) — top=45 reserva espaço
            // pro label "Duelar" no topo do botão. Softness 50,50 cria
            // borda fadeada nas laterais/topo/base — disfarça o canto
            // reto do clip retangular.
            Vector4 padding = new Vector4(0, 0, 0, 45);
            Vector2Int softness = new Vector2Int(50, 50);
            RectMask2D_padding.GetSetMethod().Invoke(
                maskComp, new IntPtr[] { new IntPtr(&padding) });
            RectMask2D_softness.GetSetMethod().Invoke(
                maskComp, new IntPtr[] { new IntPtr(&softness) });
            IntPtr containerTransform = GameObject.GetTransform(containerObj);
            Transform.SetParent(containerTransform, GameObject.GetTransform(btnObj));
            Transform.SetSiblingIndex(containerTransform, 1);
            SetFull(containerTransform);
            // Pequeno padding pra que a máscara não cole exatamente nas
            // bordas do botão (deixa as bordas do botão visíveis).
            SetSizeDelta(containerTransform, new AssetHelper.Vector2(-8, -8));
            Vector3 zero = new Vector3(0, 0, 0);
            RectTransform_anchoredPosition3D.GetSetMethod().Invoke(
                containerTransform, new IntPtr[] { new IntPtr(&zero) });
            Transform.SetLocalScale(containerTransform, new Vector3(1, 1, 1));

            // Imagem da carta dentro do container, preenchendo + preservando
            // aspect ratio. Cartas são vertical (portrait); o preserveAspect
            // faz o sprite encolher horizontalmente em vez de esticar.
            IntPtr imgObj = GameObject.New();
            UnityObject.SetName(imgObj, "Img");
            GameObject.AddComponent(imgObj, RectTransform_Type);
            IntPtr imgComp = GameObject.AddComponent(imgObj, Image_Type);
            IntPtr imgTransform = GameObject.GetTransform(imgObj);
            Transform.SetParent(imgTransform, containerTransform);
            Image_SetSprite.Invoke(imgComp, new IntPtr[] { sprite });
            csbool preserve = true;
            Image_preserveAspect.GetSetMethod().Invoke(
                imgComp, new IntPtr[] { new IntPtr(&preserve) });
            // pivot bottom-center: a imagem (com preserveAspect) encolhe
            // pelo topo, ficando alinhada à base do botão. O padding.w=45
            // do RectMask2D corta visualmente o topo (espaço pro label).
            // ATENÇÃO: setar pivot ANTES de anchors/offsets — set depois
            // faz Unity reajustar sizeDelta pra "manter visualmente".
            AssetHelper.Vector2 pivot = new AssetHelper.Vector2(0.5f, 0);
            RectTransform_pivot.GetSetMethod().Invoke(
                imgTransform, new IntPtr[] { new IntPtr(&pivot) });
            SetFull(imgTransform);
            SetSizeDelta(imgTransform, new AssetHelper.Vector2(0, 0));
            RectTransform_anchoredPosition3D.GetSetMethod().Invoke(
                imgTransform, new IntPtr[] { new IntPtr(&zero) });
            Transform.SetLocalScale(imgTransform, new Vector3(1, 1, 1));
        }

        // Layout binário compatível com UnityEngine.Vector4 (4 floats).
        // Usado pra setar RectMask2D.padding (left, bottom, right, top).
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct Vector4
        {
            public float x, y, z, w;
            public Vector4(float x, float y, float z, float w)
            { this.x = x; this.y = y; this.z = z; this.w = w; }
        }
        // Layout binário compatível com UnityEngine.Vector2Int (2 ints).
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        struct Vector2Int
        {
            public int x, y;
            public Vector2Int(int x, int y) { this.x = x; this.y = y; }
        }

        // Igual ao helper privado em SoloVisualNovelChapterView — seta
        // anchors + offsets do RectTransform numa só chamada.
        static void SetSize(IntPtr transform,
            AssetHelper.Vector2 anchorMin, AssetHelper.Vector2 anchorMax,
            AssetHelper.Vector2 offsetMin, AssetHelper.Vector2 offsetMax)
        {
            RectTransform_anchorMin.GetSetMethod().Invoke(transform, new IntPtr[] { new IntPtr(&anchorMin) });
            RectTransform_anchorMax.GetSetMethod().Invoke(transform, new IntPtr[] { new IntPtr(&anchorMax) });
            RectTransform_offsetMin.GetSetMethod().Invoke(transform, new IntPtr[] { new IntPtr(&offsetMin) });
            RectTransform_offsetMax.GetSetMethod().Invoke(transform, new IntPtr[] { new IntPtr(&offsetMax) });
        }

        static void SetFull(IntPtr transform)
        {
            AssetHelper.Vector2 min = new AssetHelper.Vector2(0, 0);
            AssetHelper.Vector2 max = new AssetHelper.Vector2(1, 1);
            AssetHelper.Vector2 zero = new AssetHelper.Vector2(0, 0);
            RectTransform_anchorMin.GetSetMethod().Invoke(transform, new IntPtr[] { new IntPtr(&min) });
            RectTransform_anchorMax.GetSetMethod().Invoke(transform, new IntPtr[] { new IntPtr(&max) });
            RectTransform_offsetMin.GetSetMethod().Invoke(transform, new IntPtr[] { new IntPtr(&zero) });
            RectTransform_offsetMax.GetSetMethod().Invoke(transform, new IntPtr[] { new IntPtr(&zero) });
        }

        static void SetSizeDelta(IntPtr transform, AssetHelper.Vector2 sizeDelta)
        {
            RectTransform_sizeDelta.GetSetMethod().Invoke(transform, new IntPtr[] { new IntPtr(&sizeDelta) });
        }
    }
}
