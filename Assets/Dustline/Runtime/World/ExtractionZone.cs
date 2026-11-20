using UnityEngine;

namespace Dustline
{
    public sealed class ExtractionZone : MonoBehaviour
    {
        public float Radius = 4;
        public ExtractionTimer Timer { get; } = new ExtractionTimer(8);
        public bool Inside { get; private set; }
        GameSession session;
        public void Initialize(GameSession owner) { session = owner; }
        void Update()
        {
            if (session.Phase != SessionPhase.Raid || session.Player == null) return;
            Vector3 offset = session.Player.transform.position - transform.position;
            offset.y = 0;
            Inside = offset.sqrMagnitude <= Radius * Radius;
            if (Timer.Step(Inside, session.Player.Health.Alive, Time.deltaTime)) session.EndRaid(true, "撤离成功");
        }
    }
}
