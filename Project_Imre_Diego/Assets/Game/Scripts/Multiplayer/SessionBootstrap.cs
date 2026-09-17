using UnityEngine;
namespace SurvivalFP
{
    public sealed class SessionBootstrap : MonoBehaviour
    {
        public GameObject sessionPrefab;
        void Awake(){if(!GameSession.Instance && sessionPrefab)Instantiate(sessionPrefab);}
    }
}
