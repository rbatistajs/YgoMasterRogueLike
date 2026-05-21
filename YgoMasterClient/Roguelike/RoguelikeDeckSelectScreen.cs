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
        const string PortalRoot = "SoloPortalUI(Clone).Root";

        const string RecommendGroup = "ButtonArea.MainGroup.RecommendGroup";

        static IL2Class _portalClass;
        delegate void Del_OnCreatedView(IntPtr thisPtr);
        static Hook<Del_OnCreatedView> _hook;
        static IntPtr _tmpType;
        static IntPtr _bindingTextType;
        static IntPtr _selectionButtonType;
        static IL2Field _selectionButton_onClick;
        static IL2Method _unityEvent_AddListener;
        static IL2Method _unityEvent_RemoveAllListeners;
        static IL2Property _behaviourEnabled;
        static IntPtr _rectType;
        static IL2Property _offsetMax;
        static System.Collections.Generic.List<RoguelikeApi.DeckOffer> _offers;
        static readonly Action[] _tileActions = { () => OnTile(0), () => OnTile(1), () => OnTile(2) };
        static bool _pending;
        static bool _ready;

        static RoguelikeDeckSelectScreen()
        {
            try
            {
                IL2Assembly asm = Assembler.GetAssembly("Assembly-CSharp");
                IL2Assembly core = Assembler.GetAssembly("UnityEngine.CoreModule");
                _portalClass = asm.GetClass("SoloPortalViewController", "YgomGame.Solo");
                _hook = new Hook<Del_OnCreatedView>(OnCreatedView, _portalClass.GetMethod("OnCreatedView"));
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
                _ready = true;
            }
            catch (Exception ex) { Console.WriteLine("[Roguelike] deckselect init EX: " + ex); }
        }

        public static void Open()
        {
            if (!_ready) { Console.WriteLine("[Roguelike] deckselect not ready"); return; }
            IntPtr manager = YgomGame.Menu.ContentViewControllerManager.GetManager();
            if (manager == IntPtr.Zero) return;
            _pending = true;
            YgomSystem.UI.ViewControllerManager.PushChildViewController(manager, "Solo/SoloMode");
        }

        static void OnCreatedView(IntPtr thisPtr)
        {
            _hook.Original(thisPtr);
            if (!_pending) return;
            _pending = false;
            try { Customize(thisPtr); }
            catch (Exception ex) { Console.WriteLine("[Roguelike] deckselect customize EX: " + ex); }
        }

        static void Customize(IntPtr portalPtr)
        {
            IntPtr go = Component.GetGameObject(portalPtr);
            IntPtr root = GameObject.FindGameObjectByPath(go, PortalRoot);
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
                // Render the boss-card art in the tile's native card slot (RawImage). Disable
                // the game's auto-binder/releaser on the clone so they don't override us.
                IntPtr imageGate = GameObject.FindGameObjectByPath(tile, "Button.Main.Mask.ImageGate");
                IntPtr thumb = GameObject.FindGameObjectByPath(tile, "Button.Main.Mask.ImageGate.SoloCardThumbMask.SoloCardThumbImage");
                if (thumb != IntPtr.Zero && offer.BossCard > 0)
                {
                    DisableComponent(imageGate, "BindingSoloCardThumb");
                    DisableComponent(thumb, "AutoReleaseCardIllust");
                    RoguelikeCardImage.SetThumb(thumb, offer.BossCard);
                }

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

        static void ShowDeck(RoguelikeApi.DeckOffer offer)
        {
            // TODO(Task 5 step 2): open the native deck viewer with offer.Main/Extra.
            Console.WriteLine("[Roguelike] ver deck: " + offer.Name + " (" + offer.Main.Count + " main)");
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
