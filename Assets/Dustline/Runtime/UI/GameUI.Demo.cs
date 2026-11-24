#if UNITY_EDITOR || DUSTLINE_DEMO_GM
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace Dustline
{
    public sealed partial class GameUI
    {
        partial void AddDemoGmButton(Transform parent, float x, float y, float width, float height)
        {
            Button(parent, "GM 测试", x, y, width, height, ShowDemoGm);
        }

        void ShowDemoGm()
        {
            if (session.Phase != SessionPhase.Menu) return;
            var root = Display(ScreenPage.TestGm, "GM test tools");
            Header(root, "GM 测试工具", "当前离线测试档 · 等级调整独立于对局统计");
            var card = At("GM grant form", root, 278, 142, 684, 461);
            Fill(card, Ink);
            var values = Label(card, "", 23, 21, 18, 642, 75, TextAnchor.MiddleLeft, Aqua);
            Label(card, "输入正整数增量", 18, 21, 119, 642, 30, color: Muted);
            var box = At("GM increment", card, 21, 155, 642, 53);
            var face = Fill(box, Plate, true);
            var value = Label(box, "", 23, 12, 0, 618, 53, TextAnchor.MiddleLeft);
            var hint = Label(box, "例如 10", 21, 12, 0, 618, 53, TextAnchor.MiddleLeft, Muted);
            var input = box.gameObject.AddComponent<InputField>();
            input.targetGraphic = face;
            input.textComponent = value;
            input.placeholder = hint;
            input.contentType = InputField.ContentType.IntegerNumber;
            input.characterLimit = 10;
            var result = Label(card, "", 18, 21, 327, 642, 88, TextAnchor.MiddleLeft, Amber);
            Button(card, "增加等级", 21, 246, 310, 53, () => Grant(true), true);
            Button(card, "增加信用点", 353, 246, 310, 53, () => Grant(false), true);
            UpdateValues();

            void Grant(bool level)
            {
                if (current != ScreenPage.TestGm || panelRoot != root || ProcessInventoryBackKey()) return;
                if (!int.TryParse(input.text, NumberStyles.None, CultureInfo.InvariantCulture, out int amount) || amount < 1)
                {
                    result.text = "请输入有效的正整数。";
                    return;
                }
                if (!session.ApplyDemoGrant(level, amount, out string error)) { result.text = error; return; }
                input.SetTextWithoutNotify("");
                result.text = level ? "等级已保存。" : "信用点已保存。";
                UpdateValues();
                UpdateCredits();
            }

            void UpdateValues()
            {
                var profile = session.Profile;
                values.text = "等级 " + session.PlayerLevel + " · 信用点 " + profile.Credits.ToString("N0")
                    + "\n测试等级加成 " + profile.GmLevelBonus + " · 实际撤离 " + profile.Extractions;
            }
        }
    }
}
#endif
