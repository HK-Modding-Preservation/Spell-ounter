using Modding;
using UnityEngine;
using System;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections;
using Satchel;
using SpellCounter.Extensions;


namespace SpellCounter
{
    public class SpellCounter : Mod, ILocalSettings<SaveSettings>, IGlobalSettings<GlobalSettings>, IMenuMod
    {
        public static SpellCounter Instance;
        public override string GetVersion() => "1.3.0";

        public static SaveSettings _settings = new SaveSettings();
        public void OnLoadLocal(SaveSettings s) => _settings = s;
        public SaveSettings OnSaveLocal() => _settings;

        public static GlobalSettings GlobalSettings { get; set; } = new GlobalSettings();
        public void OnLoadGlobal(GlobalSettings s) => GlobalSettings = s;
        public GlobalSettings OnSaveGlobal() => GlobalSettings;

        private Sprite _pogoSprite;
        public static GameObject _hudPogo;
        private Vector3 origpos;
        private bool isPogoing = false;
        private float lastPeakTime = -1f;      // Время, когда пойман 17.367
        private bool waitingForPogo = false;   // Флаг на пик
        private float pogoCooldownTimer = 0f;  // Кулдаун
        private bool lastCastingState = false; // Состояние каста из прошлого кадра
        private bool fsmSpellTriggered = false;
        private static float TextOffset = 0.225f;

        private Vector3 _geoAmountBaseLocalPos = Vector3.zero;
        private Vector3 lastHazardLocation = Vector3.zero;
        public override void Initialize()
        {
            foreach (string res in Assembly.GetExecutingAssembly().GetManifestResourceNames())
            {
                Log("Нашел ресурс: " + res);
            }
            Instance = this;
            On.HeroController.Awake += Awake;
            On.HeroController.Update += OnHeroUpdate;
            _pogoSprite = LoadSprite();

            On.DisplayItemAmount.OnEnable += OnDisplayAmount;
            On.UIManager.UIClosePauseMenu += (orig, self) => { orig(self); RedrawCounters(); };
            GlobalSettings.PropertyChanged += (s, e) => RedrawCounters();
            ModCommon.ModCommon.OnSpellHook += HandleSpellCast;
            currentState = PogoState.Ready;
        }
        private float lastZeroTime = -1f;
        private bool isTracking = false; // Флаг: пойман !первый! ноль

        private enum PogoState { Ready, WaitingForPeak, WaitingForHit }
        private PogoState currentState = PogoState.Ready;

        // Флаг для Update
        private bool spellWasCast = false;
        private bool shriekWasCast = false;
        private bool HandleSpellCast(ModCommon.ModCommon.Spell spellType)
        {
            // ModCommon поймал каст
            spellWasCast = true;
            if (spellType == ModCommon.ModCommon.Spell.Scream)
            {
                shriekWasCast = true;
            }
            return true; // true разрешить каст
        }
        private float stateStartTime = -1f;

        private void OnHeroUpdate(On.HeroController.orig_Update orig, HeroController self)
        {
            orig(self);

            float vY = self.GetComponent<Rigidbody2D>().velocity.y;
            float vX = self.GetComponent<Rigidbody2D>().velocity.x;
            bool isCasting = self.cState.casting;
            string hState = self.hero_state.ToString();
            bool isOnGround = hState == "idle" || hState == "running";
            // Режим 1: SPELL COUNTER
            Vector3 currentHazard = PlayerData.instance.hazardRespawnLocation;

            // Сброс при обновлении чекпоинта
            if (GlobalSettings.ResetMode == 1)
            {
                if (currentHazard != Vector3.zero && currentHazard != lastHazardLocation)
                {
                    lastHazardLocation = currentHazard;
                    ResetCombo();
                }
            }

            // Сброс при самом респавне
            if (GlobalSettings.ResetMode == 2 && self.cState.hazardRespawning)
            {
                ResetCombo();
            }
            if (GlobalSettings.CounterType == 1)
            {
                if (spellWasCast)
                {
                    _settings.PogoCount++;
                    UpdateHUD();
                }
            }

            // Режим 2: SHROGO
            else
            {
                if (pogoCooldownTimer > 0) pogoCooldownTimer -= Time.deltaTime;

                if (GlobalSettings.StrictMode)
                {
                    if (isOnGround)
                    {
                        currentState = PogoState.Ready;
                        ResetCombo();
                    }
                    // Цепь
                    switch (currentState)
                    {
                        case PogoState.Ready:
                            
                            if (shriekWasCast)
                            {
                                stateStartTime = Time.time;
                                currentState = PogoState.WaitingForPeak;
                            }
                            break;

                        case PogoState.WaitingForPeak:
                            if (Time.time - stateStartTime > 1.0f) { ResetCombo(); break; }
                            if (Math.Abs(vY - 17.367f) < 0.0001f)
                            {
                                stateStartTime = Time.time;
                                currentState = PogoState.WaitingForHit;
                            }
                            break;

                        case PogoState.WaitingForHit:
                            if (Time.time - stateStartTime > 0.5f) { ResetCombo(); break; }
                            if (Math.Abs(vY - 11.052f) < 0.0001f && pogoCooldownTimer <= 0f)
                            {
                                _settings.PogoCount++;
                                UpdateHUD();
                                pogoCooldownTimer = 0.5f;
                                currentState = PogoState.Ready;
                            }
                            break;
                    }
                }
                else
                {
                    // Казуальность
                    if (isOnGround)
                    {
                        currentState = PogoState.Ready;
                        ResetCombo();
                    }
                    switch (currentState)
                    {
                        case PogoState.Ready:

                            if (shriekWasCast)
                            {
                                stateStartTime = Time.time;
                                currentState = PogoState.WaitingForPeak;
                            }
                            break;

                        case PogoState.WaitingForPeak:
                            if (Time.time - stateStartTime > 1.0f) { currentState = PogoState.Ready; break; }
                            if (Math.Abs(vY - 17.367f) < 0.0001f)
                            {
                                stateStartTime = Time.time;
                                currentState = PogoState.WaitingForHit;
                            }
                            break;

                        case PogoState.WaitingForHit:
                            if (Time.time - stateStartTime > 0.5f) { currentState = PogoState.Ready; break; }

                            if (Math.Abs(vY - 11.052f) < 0.0001f && pogoCooldownTimer <= 0f)
                            {
                                _settings.PogoCount++;
                                UpdateHUD();
                                pogoCooldownTimer = 0.5f;
                                currentState = PogoState.Ready;
                            }
                            break;
                    }
                }
            }

            // Для следующего кадра
            spellWasCast = false;
            shriekWasCast = false;
        }

        public void ResetCombo()
        {
            if (_settings.PogoCount != 0)
            {
                _settings.PogoCount = 0;
                UpdateHUD();
            }
            currentState = PogoState.Ready;
            stateStartTime = -1f;
        }

        private void UpdateHUD()
        {
            if (_hudPogo != null)
            {
                _hudPogo.GetComponent<DisplayItemAmount>().textObject.text = _settings.PogoCount.ToString();
            }
        }

        private void Awake(On.HeroController.orig_Awake orig, HeroController self)
        {
            orig(self);
            var hudCanvas = GameObject.Find("_GameCameras").FindGameObjectInChildren("HudCamera").FindGameObjectInChildren("Hud Canvas");
            var prefab = GameManager.instance.inventoryFSM.gameObject.FindGameObjectInChildren("Geo");
            origpos = prefab.transform.position;
            DrawHud(prefab, hudCanvas);
        }

        private void DrawHud(GameObject prefab, GameObject hudCanvas)
        {
            var pos = GetPositionOption();
            _hudPogo = CreateStatObject("pogo", _settings.PogoCount.ToString(), prefab, hudCanvas.transform, _pogoSprite, new Vector3(pos.x, pos.y));
            _hudPogo.SetActive(GlobalSettings.ShowPogoCounter);
        }

        private GameObject CreateStatObject(string name, string text, GameObject prefab, Transform parent, Sprite sprite, Vector3 offset)
        {
            var go = UnityEngine.Object.Instantiate(prefab, parent, true);
            go.transform.position = origpos + offset;
            var renderer = go.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            var geoAmount = go.FindGameObjectInChildren("Geo Amount");
            if (geoAmount != null)
            {
                _geoAmountBaseLocalPos = geoAmount.transform.localPosition;
                ApplyTextOffset(geoAmount);
            }

            var component = go.GetComponent<DisplayItemAmount>();
            component.playerDataInt = name;
            component.textObject.text = text;
            component.textObject.fontSize = 4;

            go.SetActive(true);
            var collider = go.GetComponent<BoxCollider2D>();
            if (collider != null)
            {
                collider.size = new Vector2(1.5f, 1f);
                collider.offset = new Vector2(0.5f, 0f);
            }

            return go;
        }
        private void ApplyTextOffset(GameObject geoAmount)
        {
            if (geoAmount == null) return;
            geoAmount.transform.localPosition = _geoAmountBaseLocalPos - new Vector3(TextOffset, 0, 0);
        }
        private Sprite LoadSprite()
        {
            var resource = Assembly.GetExecutingAssembly().GetManifestResourceNames()
                            .FirstOrDefault(x => x.EndsWith("pogo.png"));

            if (string.IsNullOrEmpty(resource)) return null;

            using (Stream res = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
            {
                byte[] buffer = new byte[res.Length];
                res.Read(buffer, 0, buffer.Length);

                // В дефкаунтере было так
                var tex = new Texture2D(1, 1);
                tex.LoadImage(buffer, true);

                return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 128f);
            }
        }

        private Vector2 GetPositionOption()
        {
            if (GlobalSettings.PositionIndex == 0) return new Vector2(2.2f, 11.3f); // Beside Geo
            if (GlobalSettings.PositionIndex == 1) return new Vector2(2.2f, 10.55f); // Under Geo
            return new Vector2(-0.15f, 13.5f); // Above Masks
        }

        private void RedrawCounters()
        {
            if (_hudPogo == null) return;
            _hudPogo.SetActive(GlobalSettings.ShowPogoCounter);
            var pos = GetPositionOption();
            _hudPogo.transform.position = origpos + new Vector3(pos.x, pos.y);
        }

        private void OnDisplayAmount(On.DisplayItemAmount.orig_OnEnable orig, DisplayItemAmount self)
        {
            orig(self);
            if (self.playerDataInt == "pogo") self.textObject.text = _settings.PogoCount.ToString();
        }

        public bool ToggleButtonInsideMenu => true;

        public List<IMenuMod.MenuEntry> GetMenuData(IMenuMod.MenuEntry? toggleButtonEntry)
        {
            return new List<IMenuMod.MenuEntry>
            {
                new IMenuMod.MenuEntry
                {
                    Name = "Show Spell Counter",
                    Description = "Show/Hide the counter on HUD",
                    Values = new[] { "On", "Off" },
                    Saver = opt => {
                        GlobalSettings.ShowPogoCounter = opt == 0;
                        RedrawCounters();
                    },
                    Loader = () => GlobalSettings.ShowPogoCounter ? 0 : 1
                },
                new IMenuMod.MenuEntry
                {
                    Name = "Counter Type",
                    Description = "Shrogo: specific move | All Spells: any cast",
                    Values = new[] { "Shrogo", "All Spells" },
                    Saver = opt => {
                        GlobalSettings.CounterType = opt;
                        ResetCombo(); // Сбрасываем при смене режима (полит)
                    },
                    Loader = () => GlobalSettings.CounterType
                },
                new IMenuMod.MenuEntry
                {
                    Name = "Shrogo Logic",
                    Description = "Strict: Velocity chain | Casual: Airborne only",
                    Values = new[] { "Strict", "Casual" },
                    Saver = opt => GlobalSettings.StrictMode = opt == 0,
                    Loader = () => GlobalSettings.StrictMode ? 0 : 1
                },
                new IMenuMod.MenuEntry
                {
                    Name = "Reset On...",
                    Description = "Reset counter to 0 in All Spells mode",
                    Values = new[] { "Never", "Hazard Checkpoint", "Hazard Respawn" },
                    Saver = opt => GlobalSettings.ResetMode = opt,
                    Loader = () => GlobalSettings.ResetMode
                },
                new IMenuMod.MenuEntry
                {
                    Name = "Display Position",
                    Description = "Choose where the counter appears",
                    Values = new[] { "Beside Geo", "Under Geo", "Above Masks" },
                    Saver = opt => {
                        GlobalSettings.PositionIndex = opt;
                        RedrawCounters();
                    },
                    Loader = () => GlobalSettings.PositionIndex
                }
            };
        }
    }
}
