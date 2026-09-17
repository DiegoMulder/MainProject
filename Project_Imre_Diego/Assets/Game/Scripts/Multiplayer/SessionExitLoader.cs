using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace SurvivalFP
{
    public sealed class SessionExitLoader : MonoBehaviour
    {
        public static void LoadAfterShutdown(string scene,GameObject session)
        {
            var loader=new GameObject("Session cleanup").AddComponent<SessionExitLoader>();
            DontDestroyOnLoad(loader.gameObject);
            loader.StartCoroutine(loader.Load(scene,session));
        }
        IEnumerator Load(string scene,GameObject session)
        {
            Destroy(session); yield return null;
            SceneManager.LoadScene(scene);
            Destroy(gameObject);
        }
    }
}
