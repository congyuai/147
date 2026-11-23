using UnityEngine;
using UnityEngine.EventSystems;

namespace Dustline
{
    // UI events must reach the same frame's input snapshot before action completion.
    [DefaultExecutionOrder(-240)]
    internal sealed class DustlineEventSystem : EventSystem
    {
        internal System.Action BeforeInput;
        protected override void Update()
        {
            BeforeInput?.Invoke();
            base.Update();
        }
    }
}
