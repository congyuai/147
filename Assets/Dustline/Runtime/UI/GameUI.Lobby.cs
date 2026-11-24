using UnityEngine;
using UnityEngine.UI;

namespace Dustline
{
    public sealed partial class GameUI
    {
        RectTransform lobbyDesk;
        LobbyStage lobbyStage;
        static readonly Color LobbyPlate = new Color32(17, 28, 32, 226);
        static readonly EquipmentSlot[] LobbySlots =
        {
            EquipmentSlot.Weapon, EquipmentSlot.PrimaryTwo, EquipmentSlot.Sidearm, EquipmentSlot.Melee,
            EquipmentSlot.Helmet, EquipmentSlot.Armor, EquipmentSlot.Rig, EquipmentSlot.Pack
        };

        partial void AddDemoGmButton(Transform parent, float x, float y, float width, float height);

        void ShowLobby()
        {
            Switch(ScreenPage.Lobby);
            var desk = lobbyDesk = Stretch("Operations desk", page);
            var swipe = Stretch("Operator drag region", desk);
            Fill(swipe, Color.clear, true).canvasRenderer.cullTransparentMesh = false;
            var drag = swipe.gameObject.AddComponent<OperatorDrag>();
            var surface = Anchor("Lobby HUD", desk, .5f, .5f);
            Text level = null, value = null, weight = null;
            var slots = new Text[LobbySlots.Length];
            try
            {
                lobbyStage = desk.gameObject.AddComponent<LobbyStage>();
                lobbyStage.Initialize(transform, session, swipe, DrawHud, RefreshHud);
                drag.Turn = lobbyStage.Turn;
                Canvas.ForceUpdateCanvases();
                lobbyStage.Refresh();
            }
            catch
            {
                DisposeLobby();
                throw;
            }

            void DrawHud()
            {
                ClearContent(surface);
                float scale = Mathf.Min(1.13f, Mathf.Min(desk.rect.width / 1060f, desk.rect.height / 650f));
                surface.sizeDelta = desk.rect.size / scale;
                surface.localScale = Vector3.one * scale;
                float w = surface.rect.width, h = surface.rect.height, margin = Mathf.Clamp(w * .023f, 20f, 41f);
                float sideW = Mathf.Clamp(w * .218f, 226f, 335f), right = w - sideW - margin;
                var heading = Card("Lobby title", margin, 20, 278, 66, Amber);
                Label(heading, "DUSTLINE  /  OPERATIONS", 13, 14, 9, 249, 19, color: Aqua);
                Label(heading, "尘线：行动大厅", 26, 14, 25, 250, 35, TextAnchor.MiddleLeft);
                var profile = Card("Lobby profile", right, 20, sideW, 66, Aqua);
                level = Label(profile, "", 15, 11, 5, sideW - 23, 25, TextAnchor.MiddleRight, Muted);
                creditsReadout = Label(profile, "", 23, 11, 27, sideW - 23, 31, TextAnchor.MiddleRight, Pale);
                creditsReadout.resizeTextForBestFit = true;
                creditsReadout.resizeTextMinSize = 15;
                creditsReadout.resizeTextMaxSize = 23;

                float navW = Mathf.Clamp(w * .144f, 154f, 202f);
                float top = Mathf.Clamp(h * .208f, 116f, 156f);
                var navigation = Card("Lobby navigation", margin, top, navW, 374, Aqua);
                Label(navigation, "行动整备", 17, 11, 6, navW - 22, 26, color: Muted);
                float bw = navW - 16;
                Button(navigation, "仓库", 8, 35, bw, 40, ShowStash);
                Button(navigation, "商店", 8, 82, bw, 40, () => ShowShop());
                Button(navigation, "市场", 8, 129, bw, 40, ShowMarket);
                Button(navigation, "枪械改装", 8, 176, bw, 40, () => ShowGunsmith());
                Button(navigation, "离线靶场", 8, 223, bw, 40, session.StartPractice);
                Button(navigation, "角色", 8, 281, bw, 39, ShowOperator);
                Button(navigation, "设置", 8, 326, bw, 39, () => ShowSettings(false));
                AddDemoGmButton(surface, margin, h - 76, navW, 52);

                var summary = Card("Deployment summary", right, top, sideW, 292, Amber);
                Label(summary, "当前穿戴", 23, 11, 8, sideW - 22, 32, color: Pale);
                value = Label(summary, "", 17, 11, 42, sideW - 22, 24, color: Aqua);
                weight = Label(summary, "", 15, 11, 68, sideW - 22, 23, color: Muted);
                Fill(At("Loadout rule", summary, 10, 99, sideW - 20, 1), Border);
                for (int i = 0; i < LobbySlots.Length; i++)
                {
                    float y = 109 + i * 21.5f;
                    Label(summary, SlotName(LobbySlots[i]), 13, 11, y, 61, 21, TextAnchor.MiddleLeft, Muted);
                    var text = Label(summary, "", 14, 72, y, sideW - 82, 21, TextAnchor.MiddleLeft);
                    text.resizeTextForBestFit = true;
                    text.resizeTextMinSize = 10;
                    text.resizeTextMaxSize = 14;
                    slots[i] = text;
                }
                if (!string.IsNullOrEmpty(session.Notice))
                {
                    var notice = Card("Lobby notice", right, top + 300, sideW, 51, Aqua);
                    Label(notice, session.Notice, 13, 10, 2, sideW - 20, 47, TextAnchor.MiddleLeft, Amber);
                }
                bool ready = Deployment.TryDifficulty(session.PreparedSite, session.PreparedTier, out var tier);
                if (ready)
                {
                    string gate = tier.RequiredValue == 0 ? "可空装" : "装备 " + tier.RequiredValue.ToString("N0") + " · 等级 " + tier.RequiredLevel;
                    Label(surface, (tier.Seconds / 60).ToString("00") + ":" + (tier.Seconds % 60).ToString("00") + " · " + gate,
                        14, right, h - 149, sideW, 23, TextAnchor.MiddleRight, Pale);
                    Button(surface, Deployment.Sites[session.PreparedSite].Name + " / " + tier.Name,
                        right, h - 122, sideW, 39, OpenStrategicMap);
                }
                Button(surface, ready ? "出发" : "开始游戏", right, h - 76, sideW, 52,
                    () => { if (ready) session.StartRaid(); else OpenStrategicMap(); }, true);

                var hint = At("Operator gesture hint", surface, w * .47f - 82, h - 58, 164, 31);
                Fill(hint, new Color32(17, 28, 32, 182));
                Label(hint, "‹   横向拖动   ›", 14, 2, 1, 160, 29, TextAnchor.MiddleCenter, Pale);

                RectTransform Card(string name, float x, float y, float width, float height, Color accent)
                {
                    var rect = At(name, surface, x, y, width, height);
                    Fill(rect, LobbyPlate, true);
                    Fill(At("Accent", rect, 0, 0, 38, 2), accent);
                    return rect;
                }
            }

            void RefreshHud()
            {
                if (value == null) return;
                var bag = session.Loadout;
                level.text = "行动员等级 " + session.PlayerLevel + " / 本地突袭";
                UpdateCredits();
                value.text = "装备价值  " + bag.EquipmentValue.ToString("N0");
                weight.text = "负重  " + bag.Weight.ToString("0.0") + " / " + bag.MaxWeight.ToString("0.#") + " 千克";
                for (int i = 0; i < slots.Length; i++)
                {
                    var slot = LobbySlots[i];
                    slots[i].text = ItemName(bag.Equipped(slot), slot == EquipmentSlot.Melee ? session.BoundBladeName : "空槽");
                }
            }
        }

        void DisposeLobby()
        {
            if (lobbyStage != null) lobbyStage.Dispose();
            lobbyStage = null;
            if (lobbyDesk == null) return;
            lobbyDesk.gameObject.SetActive(false);
            Destroy(lobbyDesk.gameObject);
            lobbyDesk = null;
        }
    }
}
