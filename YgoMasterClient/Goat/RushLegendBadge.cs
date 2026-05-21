// Show a "Legend" badge ("L" icon) on any DeckCard-style thumbnail when the
// native engine flags the cid as Legend (DuelDll.CardIsLegend, which reads the
// bit baked into CARD_Prop — variants included).
//
// Two injection paths:
//   1. Prefab pre-injection — for DeckEditCard, the IconLegend child is added
//      once to DeckView.template_DeckCard on DeckView.Awake, so every clone
//      Unity spawns from it already has the badge baked in. SetData only
//      toggles SetActive.
//   2. Lazy on-first-use — for CardDetailView.CardArea (single persistent
//      preview UI), the badge is created on the first SetCard call (since
//      it isn't a prefab being cloned) and cached for subsequent toggles.

using System;
using IL2CPP;
using UnityEngine;
using YgoMaster;
using YgomGame.Deck;
using GameObject = UnityEngine.GameObject;
using ComponentRef = UnityEngine.Component;
using UnityObjectRef = UnityEngine.UnityObject;

namespace YgoMasterClient
{
    static unsafe class RushLegendBadge
    {
        const string LegendChildName = "IconLegend";
        const string TemplateChildName = "IconLimit";
        const string SpriteAssetPath = "Images/legend_icon";

        delegate void Del_DeckViewAwake(IntPtr thisPtr);
        delegate void Del_SetData(IntPtr thisPtr, ref CardBaseData baseData, int regulationID, int mode);
        delegate void Del_DetailSetCard(IntPtr thisPtr, ref CardBaseData baseData,
            int a, int b, csbool c, csbool d, csbool e, csbool f);

        static Hook<Del_DeckViewAwake> _hookAwake;
        static Hook<Del_SetData> _hookSetData;
        static Hook<Del_DetailSetCard> _hookDetailSetCard;

        static IL2Field _fieldTemplate;
        static IL2Method _imageSetSprite;
        static IntPtr _imageType;
        static IntPtr _cachedSprite;

        static RushLegendBadge()
        {
            try
            {
                IL2Assembly asm = Assembler.GetAssembly("Assembly-CSharp");
                IL2Class deckViewClass = asm.GetClass("DeckView", "YgomGame.Deck");
                IL2Class deckEditCardClass = asm.GetClass("DeckEditCard", "YgomGame.Deck");
                IL2Class cardDetailViewClass = asm.GetClass("CardDetailView", "YgomGame.Deck");
                _fieldTemplate = deckViewClass?.GetField("template_DeckCard");

                IL2Class imageClass = Assembler.GetAssembly("UnityEngine.UI")
                    .GetClass("Image", "UnityEngine.UI");
                _imageSetSprite = imageClass.GetProperty("sprite").GetSetMethod();
                _imageType = imageClass.IL2Typeof();

                IL2Method onAwake = deckViewClass?.GetMethod("Awake");
                IL2Method onSetData = deckEditCardClass?.GetMethod("SetData");
                // CardDetailView has multiple SetCard overloads; pick the
                // 7-param one (CardBaseData, int, int, bool, bool, bool, bool).
                IL2Method onDetailSetCard = cardDetailViewClass?.GetMethod("SetCard",
                    m => m.GetParameters().Length == 7);
                if (_fieldTemplate == null || onAwake == null || onSetData == null
                    || onDetailSetCard == null || _imageSetSprite == null)
                {
                    Console.WriteLine("[RushLegendBadge] pieces missing — disabled");
                    return;
                }

                _hookAwake         = new Hook<Del_DeckViewAwake>(OnDeckViewAwake, onAwake);
                _hookSetData       = new Hook<Del_SetData>(OnSetData, onSetData);
                _hookDetailSetCard = new Hook<Del_DetailSetCard>(OnDetailSetCard, onDetailSetCard);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[RushLegendBadge] init EX: " + ex);
            }
        }

        static void OnDeckViewAwake(IntPtr deckView)
        {
            _hookAwake.Original(deckView);
            try { InjectBadgeIntoTemplate(deckView); }
            catch (Exception ex) { Console.WriteLine("[RushLegendBadge] inject EX: " + ex); }
        }

        static void OnSetData(IntPtr card, ref CardBaseData data, int regulationID, int mode)
        {
            _hookSetData.Original(card, ref data, regulationID, mode);
            ToggleBadge(ComponentRef.GetGameObject(card), data.CardID);
        }

        static void OnDetailSetCard(IntPtr detailView, ref CardBaseData data,
            int a, int b, csbool c, csbool d, csbool e, csbool f)
        {
            _hookDetailSetCard.Original(detailView, ref data, a, b, c, d, e, f);
            IntPtr windowGo = ComponentRef.GetGameObject(detailView);
            IntPtr cardAreaGo = GameObject.FindGameObjectByName(windowGo, "CardArea");
            if (cardAreaGo == IntPtr.Zero) return;
            ToggleBadge(cardAreaGo, data.CardID);
        }

        // Toggle the IconLegend child on/off based on Legend status. If the
        // child doesn't exist yet (e.g. persistent UI not spawned from a
        // pre-injected template), clone IconLimit on the fly.
        static void ToggleBadge(IntPtr go, int cardId)
        {
            if (go == IntPtr.Zero) return;
            bool isLegend = cardId > 0 && DuelDll.CardIsLegend(cardId);
            IntPtr badge = GameObject.FindGameObjectByName(go, LegendChildName);
            if (badge == IntPtr.Zero && isLegend)
                badge = CreateBadge(go);
            if (badge != IntPtr.Zero)
                GameObject.SetActive(badge, isLegend);
        }

        // Reads DeckView.template_DeckCard, finds its IconLimit child, clones
        // it as a sibling named IconLegend with our PNG as sprite. Idempotent.
        static void InjectBadgeIntoTemplate(IntPtr deckView)
        {
            IL2Object templateObj = _fieldTemplate.GetValue(deckView);
            if (templateObj == null || templateObj.ptr == IntPtr.Zero) return;
            IntPtr templateGo = ComponentRef.GetGameObject(templateObj.ptr);
            if (templateGo == IntPtr.Zero) return;
            CreateBadge(templateGo, activeOnCreate: false);
        }

        // Clones IconLimit under the same parent as IconLegend sibling.
        // Returns the clone (or Zero if no template). Caller decides active state.
        static IntPtr CreateBadge(IntPtr root, bool activeOnCreate = true)
        {
            IntPtr existing = GameObject.FindGameObjectByName(root, LegendChildName);
            if (existing != IntPtr.Zero) return existing;

            IntPtr iconLimit = GameObject.FindGameObjectByName(root, TemplateChildName);
            if (iconLimit == IntPtr.Zero) return IntPtr.Zero;

            IntPtr parentTf = UnityEngine.Transform.GetParent(GameObject.GetTransform(iconLimit));
            IntPtr clone = UnityObjectRef.Instantiate(iconLimit, parentTf);
            UnityObjectRef.SetName(clone, LegendChildName);

            IntPtr image = GameObject.GetComponent(clone, _imageType);
            IntPtr sprite = LoadSprite();
            if (image != IntPtr.Zero && sprite != IntPtr.Zero)
                _imageSetSprite.Invoke(image, new IntPtr[] { sprite });
            GameObject.SetActive(clone, activeOnCreate);
            return clone;
        }

        // Loads the badge icon through the game's ResourceManager (same pattern as
        // SoloChapterCardImage): the AssetHelper Load hook serves our custom PNG
        // (ClientData/Images/legend_icon.png) and the ResourceManager owns the
        // texture, so Unity won't unload it on deck/scene transitions. Only the
        // sprite we create needs anchoring.
        static IntPtr LoadSprite()
        {
            if (_cachedSprite != IntPtr.Zero) return _cachedSprite;
            IntPtr texture = AssetHelper.LoadImmediateAsset(SpriteAssetPath);
            if (texture == IntPtr.Zero) return IntPtr.Zero;
            IntPtr sprite = AssetHelper.SpriteFromTexture(texture, "legend_icon",
                default(AssetHelper.Rect), 100);
            if (sprite == IntPtr.Zero) return IntPtr.Zero;
            Import.Handler.il2cpp_gchandle_new(sprite, true);
            UnityObjectRef.DontDestroyOnLoad(sprite);
            _cachedSprite = sprite;
            return _cachedSprite;
        }
    }
}
