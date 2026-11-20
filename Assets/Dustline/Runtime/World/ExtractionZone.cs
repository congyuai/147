using UnityEngine;

namespace Dustline
{
    public sealed class ExtractionZone : MonoBehaviour
    {
        public float Radius = 4;
        public string DisplayName { get; private set; } = "撤离点";
        public ExtractionTimer Timer { get; } = new ExtractionTimer(8);
        public bool Inside { get; private set; }
        GameSession session;

        public void Initialize(GameSession owner)
        {
            session = owner;
        }

        internal void Initialize(GameSession owner, PortExit exit)
        {
            session = owner;
            DisplayName = exit.Name;
            Radius = exit.Radius;
        }

        public bool Contains(Vector3 position)
        {
            Vector3 offset = position - transform.position;
            return offset.y >= -0.4f && offset.y <= 1.2f && offset.x * offset.x + offset.z * offset.z <= Radius * Radius;
        }

        void Update()
        {
            var player = session != null && session.Phase == SessionPhase.Raid ? session.Player : null;
            bool alive = player != null && player.Health != null && player.Health.Alive;
            Inside = alive && Contains(player.transform.position);
            if (Timer.Step(Inside, alive, Time.deltaTime)) session.EndRaid(true, "撤离成功：" + DisplayName);
        }
    }
}
