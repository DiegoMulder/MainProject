using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
namespace SurvivalFP
{
    // URP still renders the scene normally, into a genuinely smaller camera target.
    // IMGUI is composed afterwards at screen resolution, outside the world filter.
    [RequireComponent(typeof(Camera)), DefaultExecutionOrder(900)]
    public sealed class PsxCameraPresentation : MonoBehaviour
    {
        public PsxVisualProfile profile;
        public Vector2Int InternalSize=>target?new Vector2Int(target.width,target.height):Vector2Int.zero;
        Camera view; RenderTexture target, previousTarget; Material material;
        Camera output; RawImage image;
        bool configured,oldMSAA,oldHDR; float oldAspect;
        UniversalAdditionalCameraData cameraData; AntialiasingMode oldAA;
        void Awake(){view=GetComponent<Camera>();cameraData=GetComponent<UniversalAdditionalCameraData>();}
        void LateUpdate()
        {
            if(!profile || !profile.effectEnabled || !view.enabled || !view.gameObject.activeInHierarchy){Release();return;}
            int height=Mathf.Min(Screen.height,Mathf.Clamp(profile.internalHeight,120,720));
            int width=Mathf.Max(1,Mathf.RoundToInt(height*(float)Screen.width/Mathf.Max(1,Screen.height)));
            if(!configured)
            {
                previousTarget=view.targetTexture;oldMSAA=view.allowMSAA;oldHDR=view.allowHDR;oldAspect=view.aspect;
                if(cameraData){oldAA=cameraData.antialiasing;cameraData.antialiasing=AntialiasingMode.None;}
                view.allowMSAA=false;view.allowHDR=false;configured=true;
            }
            if(!target || target.width!=width || target.height!=height)
            {
                if(target){view.targetTexture=null;target.Release();Destroy(target);}
                target=new RenderTexture(width,Mathf.Max(1,height),24,RenderTextureFormat.ARGB32)
                {name="PSX world",filterMode=FilterMode.Point,antiAliasing=1,wrapMode=TextureWrapMode.Clamp};
                target.Create();view.targetTexture=target;
            }
            view.aspect=(float)Screen.width/Mathf.Max(1,Screen.height);
            if(profile.presentationShader && (!material || material.shader!=profile.presentationShader))
            {if(material)Destroy(material);material=new Material(profile.presentationShader);}
            UpdateOutput();
        }
        void UpdateOutput()
        {
            if(!output)
            {
                var root=new GameObject("PSX screen output",typeof(Camera));
                root.hideFlags=HideFlags.DontSave;
                output=root.GetComponent<Camera>();output.orthographic=true;
                output.nearClipPlane=.01f;output.farClipPlane=2f;
                output.cullingMask=1<<5;output.clearFlags=CameraClearFlags.SolidColor;
                output.backgroundColor=Color.black;output.allowHDR=false;output.allowMSAA=false;
                output.GetUniversalAdditionalCameraData().renderPostProcessing=false;
                var canvasObject=new GameObject("World image",typeof(Canvas));
                canvasObject.layer=5;
                canvasObject.transform.SetParent(root.transform,false);
                var canvas=canvasObject.GetComponent<Canvas>();
                canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=output;canvas.planeDistance=.5f;
                var quad=new GameObject("Pixels",typeof(RectTransform),typeof(CanvasRenderer),typeof(RawImage));
                quad.layer=5;
                quad.transform.SetParent(canvasObject.transform,false);
                image=quad.GetComponent<RawImage>();image.raycastTarget=false;
                image.rectTransform.anchorMin=Vector2.zero;image.rectTransform.anchorMax=Vector2.one;
                image.rectTransform.offsetMin=image.rectTransform.offsetMax=Vector2.zero;
            }
            output.depth=view.depth+1;output.targetDisplay=view.targetDisplay;output.rect=view.rect;
            image.texture=target;image.material=material;
            if(material)
            {
                material.SetFloat("_Levels",Mathf.Clamp(profile.colourLevels,4,64));
                material.SetFloat("_Dither",Mathf.Clamp01(profile.dithering));
            }
        }
        void Release()
        {
            if(configured && view)
            {
                view.targetTexture=previousTarget;view.allowMSAA=oldMSAA;view.allowHDR=oldHDR;view.aspect=oldAspect;
                if(cameraData)cameraData.antialiasing=oldAA;
            }
            configured=false;
            if(output){output.enabled=false;Destroy(output.gameObject);output=null;image=null;}
            if(target){target.Release();Destroy(target);target=null;}
        }
        void OnDisable()=>Release();
        void OnDestroy(){Release();if(material)Destroy(material);}
    }
}
