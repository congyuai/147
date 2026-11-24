using System;
using UnityEngine;
using UnityEngine.UI;

namespace Dustline
{
    public sealed partial class GameUI
    {
        void AddAmmoPurchaseControls(RectTransform popup, ItemDefinition item, GridInventory destination, string confirmLabel,
            Func<bool> canAct, Func<int, int, (bool ok, string error)> purchase, Action purchased = null)
        {
            int quote = item.Value, amount = 1, limit = 0, stamp = 0;
            bool submitting = false;
            int limitRevision = -1, limitCredits = -1;
            string limitError = null;
            popup.sizeDelta = new Vector2(330, 309);
            var quantity = Label(popup, "", 16, 13, 164, 304, 22, TextAnchor.MiddleLeft, Aqua);
            quantity.name = "Ammo amount";
            var minus = Button(popup, "−", 13, 188, 34, 36, null);
            minus.name = "Ammo minus";
            var plus = Button(popup, "+", 216, 188, 34, 36, null);
            plus.name = "Ammo plus";
            var max = Button(popup, "最大", 256, 188, 61, 36, null);
            max.name = "Ammo max";

            var lane = At("Ammo quantity", popup, 53, 188, 157, 36);
            Fill(lane, Surface, true);
            var track = At("Quantity track", lane, 8, 15, 141, 6);
            Fill(track, Border);
            var fill = Fill(Stretch("Quantity fill", track), Aqua);
            var handleArea = Stretch("Quantity handle area", lane);
            handleArea.offsetMin = new Vector2(8, 0);
            handleArea.offsetMax = new Vector2(-8, 0);
            var handle = Anchor("Quantity handle", handleArea, 0, .5f);
            handle.sizeDelta = new Vector2(16, 24);
            var knob = Fill(handle, Aqua);
            var slider = lane.gameObject.AddComponent<Slider>();
            slider.targetGraphic = knob;
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle;
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.wholeNumbers = true;
            slider.minValue = 1;

            Label(popup, "单价 " + quote.ToString("N0") + " / 发", 15, 13, 227, 146, 21, TextAnchor.MiddleLeft, Muted).name = "Ammo unit price";
            var balance = Label(popup, "", 15, 164, 227, 153, 21, TextAnchor.MiddleRight, Aqua);
            balance.name = "Ammo balance";
            var total = Label(popup, "", 17, 13, 247, 304, 23, TextAnchor.MiddleLeft, Amber);
            total.name = "Ammo total";
            var confirmRect = At("Ammo confirm", popup, 13, 270, 304, 32);
            Label(confirmRect, confirmLabel, 18, 5, 0, 294, 32, TextAnchor.MiddleCenter);
            inventoryTiles.RemoveAll(tile => tile == null || !tile.gameObject.activeInHierarchy);
            var confirm = CreateItemTile(confirmRect, Amber);
            StyleButton(confirm, true, Amber);
            confirm.CanPress = Ready;
            confirm.SelectionStamp = () => stamp;
            RefreshLimit();
            minus.onClick.AddListener(() => { if (Ready()) SetAmount(amount - 1); });
            plus.onClick.AddListener(() => { if (Ready()) SetAmount(amount + 1); });
            max.onClick.AddListener(() =>
            {
                if (!Ready()) return;
                RefreshLimit();
                SetAmount(limit);
            });
            slider.onValueChanged.AddListener(value =>
            {
                if (Ready()) SetAmount(Mathf.RoundToInt(value));
                else slider.SetValueWithoutNotify(Mathf.Max(1, amount));
            });
            confirm.onClick.AddListener(() =>
            {
                if (!Ready() || amount < 1 || !confirm.interactable) return;
                submitting = true;
                var result = purchase(amount, quote);
                if (result.ok)
                {
                    confirm.interactable = false;
                    purchased?.Invoke();
                    return;
                }
                submitting = false;
                if (popup == null || itemPopup != popup || session.Phase != SessionPhase.Menu) return;
                RefreshLimit();
                Notify(result.error ?? "未能保存购买结果。");
            });

            bool Ready() => !submitting && session.Phase == SessionPhase.Menu && popup != null && itemPopup == popup
                && popup.gameObject.activeInHierarchy && InventoryIdle() && canAct() && backHandledFrame != Time.frameCount;

            void RefreshLimit()
            {
                if (item.Value != quote || item.NotForSale)
                {
                    limit = 0;
                    limitError = "商品或价格已变化，请重新打开详情。";
                    limitRevision = -1;
                }
                else if (limitRevision != destination.Revision || limitCredits != session.Profile.Credits)
                {
                    limitRevision = destination.Revision;
                    limitCredits = session.Profile.Credits;
                    limit = session.ShopPurchaseLimit(item, destination, out limitError);
                }
                slider.maxValue = Mathf.Max(1, limit);
                SetAmount(amount);
            }

            void SetAmount(int next)
            {
                int previous = amount;
                amount = limit > 0 ? Mathf.Clamp(next, 1, limit) : 0;
                if (amount != previous) stamp++;
                slider.SetValueWithoutNotify(Mathf.Max(1, amount));
                slider.interactable = limit > 1;
                minus.interactable = amount > 1;
                plus.interactable = amount < limit;
                max.interactable = limit > 0;
                confirm.interactable = limit > 0;
                quantity.text = limitError ?? ("数量 " + amount + " 发 · 最多 " + limit.ToString("N0") + " 发");
                balance.text = "余额 " + session.Profile.Credits.ToString("N0");
                total.text = "总价 " + ((long)quote * amount).ToString("N0") + " 信用点";
            }
        }
    }
}
