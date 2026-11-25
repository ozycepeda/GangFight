using UnityEngine;

namespace GangFight
{
    public sealed class FighterContact : MonoBehaviour
    {
        internal Fighter Owner { get; set; }
        private void OnCollisionEnter(Collision collision) => Owner?.OnCollision(collision);
        private void OnCollisionStay(Collision collision) => Owner?.OnCollision(collision);
    }
}
