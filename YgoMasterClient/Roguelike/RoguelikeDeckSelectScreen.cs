using IL2CPP;
using System;
using UnityEngine;
using YgoMaster;

namespace YgoMasterClient
{
    // Roguelike deck-select screen. Reuses the game's Solo/SoloMode screen (animated bg +
    // header) and repurposes SoloPortal's RecommendGroup tiles as the deck choices. The
    // OnCreatedView hook only customizes when _pending is set by Open() — normal Solo mode
    // is untouched.
    static unsafe class RoguelikeDeckSelectScreen
    {
        const string RecommendGroup = "ButtonArea.MainGroup.RecommendGroup";

        static IntPtr _tmpType;
        static IntPtr _bindingTextType;
        static IntPtr _selectionButtonType;
        static IL2Field _selectionButton_onClick;
        static IL2Method _unityEvent_AddListener;
        static IL2Method _unityEvent_RemoveAllListeners;
        static IL2Property _behaviourEnabled;
        static IntPtr _rectType;
        static IL2Property _offsetMax;
        static IL2Class _objectClass;
        static IL2Class _boolClass;
        static IL2Class _dictClass;
        static IL2Method _dictCtor;
        static System.Collections.Generic.List<RoguelikeApi.DeckOffer> _offers;
        static IntPtr _recommendGroup;
        static bool _viewing;
        static readonly Action[] _tileActions = { () => OnTile(0), () => OnTile(1), () => OnTile(2) };
        static bool _ready;

        static RoguelikeDeckSelectScreen()
        {
            try
            {
                IL2Assembly asm = Assembler.GetAssembly("Assembly-CSharp");
                IL2Assembly core = Assembler.GetAssembly("UnityEngine.CoreModule");
                _tmpType = CastUtils.IL2Typeof("ExtendedTextMeshProUGUI", "YgomSystem.YGomTMPro", "Assembly-CSharp");
                _bindingTextType = CastUtils.IL2Typeof("BindingTextMeshProUGUI", "YgomSystem.UI", "Assembly-CSharp");
                IL2Class selectionButton = asm.GetClass("SelectionButton", "YgomSystem.UI");
                _selectionButtonType = selectionButton.IL2Typeof();
                _selectionButton_onClick = selectionButton.GetField("onClick");
                _unityEvent_AddListener = core.GetClass("UnityEvent", "UnityEngine.Events").GetMethod("AddListener");
                // RemoveAllListeners is declared on the base UnityEventBase, not UnityEvent.
                _unityEvent_RemoveAllListeners = core.GetClass("UnityEventBase", "UnityEngine.Events").GetMethod("RemoveAllListeners");
                _behaviourEnabled = core.GetClass("Behaviour", "UnityEngine").GetProperty("enabled");
                IL2Class rectClass = core.GetClass("RectTransform", "UnityEngine");
                _rectType = rectClass.IL2Typeof();
                _offsetMax = rectClass.GetProperty("offsetMax");
                IL2Assembly mscorlib = Assembler.GetAssembly("mscorlib");
                _objectClass = mscorlib.GetClass("Object", "System");
                _boolClass = mscorlib.GetClass("Boolean", "System");
                _dictClass = IL2Dictionary<string, object>.Instance_Class;
                _dictCtor = _dictClass.GetMethod(".ctor", x => x.GetParameters().Length == 0);
                _ready = true;
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] deckselect init EX: " + ex); }
        }

        public static void Open()
        {
            if (!_ready) { Console.WriteLine("[Roguelike] deckselect not ready"); return; }
            RoguelikeSoloScreen.Open(Customize);
        }

        static void Customize(IntPtr portalPtr)
        {
            IntPtr root = RoguelikeSoloScreen.PortalRoot(portalPtr);
            if (root == IntPtr.Zero) { Console.WriteLine("[Roguelike] portal root not found"); return; }

            SetText(root, "TitleSafeArea.TitleGroup.NameText", "Escolha seu Deck");
            Hide(root, "ButtonArea.MainGroup.LastPlayGroup");
            Hide(root, "ButtonArea.GateListGroup");
            PopulateTiles(root);
            Console.WriteLine("[Roguelike] deckselect customized");
        }

        // Build up to 3 deck tiles in RecommendGroup by cloning RecommendButton1, then hide
        // the original recommend buttons. Idempotent (reuses RgTile{i} on re-open).
        static void PopulateTiles(IntPtr root)
        {
            _offers = RoguelikeApi.GetDeckOffers();
            IntPtr group = GameObject.FindGameObjectByPath(root, RecommendGroup);
            if (group == IntPtr.Zero) { Console.WriteLine("[Roguelike] RecommendGroup not found"); return; }
            _recommendGroup = group;
            SetText(group, "RecommendText", "Decks");
            HideByName(group, "Base"); // panel bg is sized for 2 tiles; hide for now (revisit panel later)

            IntPtr template = GameObject.FindGameObjectByName(group, "RecommendButton1", false, false);
            if (template == IntPtr.Zero) { Console.WriteLine("[Roguelike] RecommendButton1 template not found"); return; }

            for (int i = 0; i < 3; i++)
            {
                string tileName = "RgTile" + i;
                IntPtr tile = GameObject.FindGameObjectByName(group, tileName, false, false);
                if (tile == IntPtr.Zero)
                {
                    tile = UnityObject.Instantiate(template, GameObject.GetTransform(group));
                    UnityObject.SetName(tile, tileName);
                }
                if (i < _offers.Count)
                {
                    GameObject.SetActive(tile, true);
                    SetupTile(tile, _offers[i], i);
                }
                else GameObject.SetActive(tile, false);
            }
            HideByName(group, "RecommendButton1");
            HideByName(group, "RecommendButton2");
        }

        static void SetupTile(IntPtr tile, RoguelikeApi.DeckOffer offer, int index)
        {
            try
            {
                IntPtr nameTmp = GameObject.FindGameObjectByPath(tile, "Button.Main.NameArea.NameLayoutGroup.TextGateName");
                if (nameTmp != IntPtr.Zero)
                {
                    IntPtr c = GameObject.GetComponent(nameTmp, _tmpType);
                    if (c != IntPtr.Zero) TMPro.TMP_Text.SetText(c, offer.Name);
                }
                ApplyTileArt(tile, offer);

                IntPtr buttonGo = GameObject.FindGameObjectByPath(tile, "Button");
                IntPtr sel = buttonGo == IntPtr.Zero ? IntPtr.Zero : GameObject.GetComponent(buttonGo, _selectionButtonType);
                if (sel != IntPtr.Zero)
                {
                    IL2Object onClickObj = _selectionButton_onClick.GetValue(sel);
                    IntPtr onClick = onClickObj != null ? onClickObj.ptr : IntPtr.Zero;
                    if (onClick != IntPtr.Zero)
                    {
                        if (_unityEvent_RemoveAllListeners != null) _unityEvent_RemoveAllListeners.Invoke(onClick);
                        IntPtr cb = UnityEngine.Events._UnityAction.CreateUnityAction(_tileActions[index]);
                        _unityEvent_AddListener.Invoke(onClick, new IntPtr[] { cb });
                    }
                }
                Console.WriteLine("[Roguelike] tile " + index + " setup: " + offer.Name + " boss=" + offer.BossCard);
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] SetupTile " + index + " EX: " + ex.Message); }
        }

        static int _drawerIndex;

        // Render the boss-card art in the tile's native card slot (RawImage). Disable the
        // game's auto-binder/releaser on the clone so they don't override us.
        static void ApplyTileArt(IntPtr tile, RoguelikeApi.DeckOffer offer)
        {
            if (offer.BossCard <= 0) return;
            IntPtr imageGate = GameObject.FindGameObjectByPath(tile, "Button.Main.Mask.ImageGate");
            if (imageGate == IntPtr.Zero) return;
            DisableComponent(imageGate, "BindingSoloCardThumb"); // stop the game's thumb binder on the clone
            RoguelikeCardImage.AttachCardImage(imageGate, offer.BossCard, "RgCardArt");
        }

        // The native deck viewer (DeckBrowser) releases shared card textures when it closes,
        // blanking our tiles. Re-apply the art when we return from it.
        public static void OnPopChildViewController()
        {
            if (!_viewing) return;
            _viewing = false;
            if (_recommendGroup == IntPtr.Zero || _offers == null) return;
            try
            {
                for (int i = 0; i < _offers.Count && i < 3; i++)
                {
                    IntPtr tile = GameObject.FindGameObjectByName(_recommendGroup, "RgTile" + i, false, false);
                    if (tile != IntPtr.Zero) ApplyTileArt(tile, _offers[i]);
                }
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] reapply art EX: " + ex); }
        }

        static void OnTile(int index)
        {
            if (_offers == null || index < 0 || index >= _offers.Count) return;
            _drawerIndex = index;
            RoguelikeApi.DeckOffer offer = _offers[index];
            string title = offer.Name + (string.IsNullOrEmpty(offer.Description) ? "" : "\n" + offer.Description);
            string[] entries = { "Ver deck", "Selecionar" };
            YgomGame.Menu.ActionSheetViewController.Open(title, entries, OnDrawerSelect);
        }

        static void OnDrawerSelect(IntPtr ctx, int choice)
        {
            if (_offers == null || _drawerIndex < 0 || _drawerIndex >= _offers.Count) return;
            if (choice == 0) ShowDeck(_offers[_drawerIndex]);
            else if (choice == 1) Select(_drawerIndex);
        }

        static void Select(int index)
        {
            RoguelikeApi.ChooseDeck(index);
            IntPtr manager = YgomGame.Menu.ContentViewControllerManager.GetManager();
            if (manager != IntPtr.Zero) YgomSystem.UI.ViewControllerManager.PopChildViewController(manager);
        }

        // Open the game's native read-only deck viewer (DeckBrowser / "Deck do Oponente")
        // with the offer's cards. Args are built as typed IL2CPP objects (Int32, not the
        // JSON round-trip's Int64) because DeckBrowserViewController hard-casts to Int32.
        static void ShowDeck(RoguelikeApi.DeckOffer offer)
        {
            if (!_ready) return;
            IntPtr manager = YgomGame.Menu.ContentViewControllerManager.GetManager();
            if (manager == IntPtr.Zero) return;
            try
            {
                IntPtr args = NewDict();
                DictAdd(args, "name", new IL2String(offer.Name).ptr);
                DictAdd(args, "deckNameInit", BoxBool(true));
                DictAdd(args, "iconDeckId", BoxInt(1081001));
                DictAdd(args, "mcards", CardDict(offer.Main));
                DictAdd(args, "ecards", CardDict(offer.Extra));
                DictAdd(args, "numMainCards", BoxInt(offer.Main.Count));
                DictAdd(args, "numExtraCards", BoxInt(offer.Extra.Count));
                DictAdd(args, "regulationMonochromeEnable", BoxBool(false));
                _viewing = true;
                YgomSystem.UI.ViewControllerManager.PushChildViewControllerArgs(manager, "DeckBrowser", args);
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] ShowDeck EX: " + ex); }
        }

        // --- typed IL2CPP arg builders ---
        static IntPtr NewDict()
        {
            IntPtr p = Import.Object.il2cpp_object_new(_dictClass.ptr);
            _dictCtor.Invoke(p);
            return p;
        }

        static void DictAdd(IntPtr dict, string key, IntPtr valueObj)
        {
            new IL2Dictionary<string, object>(dict).Add(new IL2String(key).ptr, valueObj);
        }

        static IntPtr BoxInt(int v) { return Import.Object.CreateNewObject<int>(v, IL2SystemClass.Int32); }
        static IntPtr BoxBool(bool v) { return Import.Object.CreateNewObject<bool>(v, _boolClass); }

        // Player-format deck section { ids:[...], r:[...] } as List<object> of boxed Int32.
        static IntPtr CardDict(System.Collections.Generic.List<int> ids)
        {
            IL2ListExplicit idsList = new IL2ListExplicit(IntPtr.Zero, _objectClass, true);
            IL2ListExplicit rList = new IL2ListExplicit(IntPtr.Zero, _objectClass, true);
            foreach (int id in ids) { idsList.Add(BoxInt(id)); rList.Add(BoxInt(1)); }
            IntPtr d = NewDict();
            DictAdd(d, "ids", idsList.ptr);
            DictAdd(d, "r", rList.ptr);
            return d;
        }

        static void HideByName(IntPtr parent, string name)
        {
            IntPtr o = GameObject.FindGameObjectByName(parent, name, false, false);
            if (o != IntPtr.Zero) GameObject.SetActive(o, false);
        }

        // Widen the RecommendGroup's "Base" panel background (sized for 2 tiles by default).
        static void SetBaseWidth(IntPtr group, float offsetMaxX)
        {
            IntPtr baseGo = GameObject.FindGameObjectByName(group, "Base", false, false);
            if (baseGo == IntPtr.Zero) return;
            IntPtr rt = GameObject.GetComponent(baseGo, _rectType);
            if (rt == IntPtr.Zero || _offsetMax == null) return;
            AssetHelper.Vector2 v = new AssetHelper.Vector2(offsetMaxX, 0);
            _offsetMax.GetSetMethod().Invoke(rt, new IntPtr[] { new IntPtr(&v) });
        }

        // Disable a Behaviour on `go` by its class name (no namespace needed) so the game's
        // data-binding components don't override our injected content.
        static void DisableComponent(IntPtr go, string className)
        {
            if (go == IntPtr.Zero || _behaviourEnabled == null) return;
            IntPtr[] comps = GameObject.GetComponents(go);
            if (comps == null) return;
            foreach (IntPtr c in comps)
            {
                string n = System.Runtime.InteropServices.Marshal.PtrToStringAnsi(
                    Import.Class.il2cpp_class_get_name(Import.Object.il2cpp_object_get_class(c)));
                if (n == className)
                {
                    csbool f = false;
                    _behaviourEnabled.GetSetMethod().Invoke(c, new IntPtr[] { new IntPtr(&f) });
                    return;
                }
            }
        }

        static void Hide(IntPtr root, string path)
        {
            IntPtr o = GameObject.FindGameObjectByPath(root, path);
            if (o != IntPtr.Zero) GameObject.SetActive(o, false);
        }

        // Binding-aware: a BindingTextMeshProUGUI overwrites a plain TMP_Text.SetText, so set
        // the literal through the binding (renders verbatim) when present; else set TMP text.
        static void SetText(IntPtr root, string path, string text)
        {
            IntPtr o = GameObject.FindGameObjectByPath(root, path);
            if (o == IntPtr.Zero) return;
            IntPtr binding = GameObject.GetComponent(o, _bindingTextType);
            if (binding != IntPtr.Zero) { YgomSystem.UI.BindingTextMeshProUGUI.SetTextId(binding, text); return; }
            IntPtr tmp = GameObject.GetComponent(o, _tmpType);
            if (tmp != IntPtr.Zero) TMPro.TMP_Text.SetText(tmp, text);
        }
    }
}
