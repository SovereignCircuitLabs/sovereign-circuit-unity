using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FadeObjectBlockingObject : Singleton<FadeObjectBlockingObject>
{
    [SerializeField] private LayerMask LayerMask;
    public Transform TargetTransform;
    [SerializeField] private Camera Camera;
    [SerializeField] private float FadedAlpha = 0.33f;
    [SerializeField] private FadeMode FadingMode;

    [SerializeField] private float ChecksPerSecond = 10;
    [SerializeField] private int FadeFPS = 30;
    [SerializeField] private float FadeSpeed = 1;

    [Header("Read Only Data")] [SerializeField]
    private List<FadingObject> ObjectsBlockingView = new List<FadingObject>();

    private List<int> IndexesToClear = new List<int>();
    private Dictionary<FadingObject, Coroutine> RunningCoroutines = new Dictionary<FadingObject, Coroutine>();

    private RaycastHit[] Hits = new RaycastHit[10];

    private void Start()
    {
        StartCoroutine(CheckForObjects());
    }

    private IEnumerator CheckForObjects()
    {
        WaitForSeconds Wait = new WaitForSeconds(1f / ChecksPerSecond);

        while (true)
        {
            if (TargetTransform == null)
            {
                RestoreAllBlockingObjects();
                ClearHits();
                yield return Wait;
                continue;
            }

            int hits = Physics.RaycastNonAlloc(Camera.transform.position,
                (TargetTransform.transform.position - Camera.transform.position).normalized, Hits,
                Vector3.Distance(Camera.transform.position, TargetTransform.transform.position), LayerMask);
            if (hits > 0)
            {
                for (int i = 0; i < hits; i++)
                {
                    FadingObject fadingObject = GetFadingObjectFromHit(Hits[i]);
                    if (fadingObject != null && !ObjectsBlockingView.Contains(fadingObject))
                    {
                        if (RunningCoroutines.ContainsKey(fadingObject))
                        {
                            if (RunningCoroutines[fadingObject] != null) // may be null if it's already ended
                            {
                                StopCoroutine(RunningCoroutines[fadingObject]);
                            }

                            RunningCoroutines.Remove(fadingObject);
                        }

                        RunningCoroutines.Add(fadingObject, StartCoroutine(FadeObjectOut(fadingObject)));
                        ObjectsBlockingView.Add(fadingObject);
                    }
                }
            }

            FadeObjectsNoLongerBeingHit();

            ClearHits();

            yield return Wait;
        }
    }

    private void RestoreAllBlockingObjects()
    {
        foreach (FadingObject fadingObject in ObjectsBlockingView)
        {
            if (fadingObject == null) continue;

            if (RunningCoroutines.TryGetValue(fadingObject, out Coroutine coroutine) && coroutine != null)
            {
                StopCoroutine(coroutine);
            }

            StartCoroutine(FadeObjectIn(fadingObject));
        }

        ObjectsBlockingView.Clear();
        RunningCoroutines.Clear();
    }

    private void FadeObjectsNoLongerBeingHit()
    {
        for (int i = 0; i < ObjectsBlockingView.Count; i++)
        {
            bool objectIsBeingHit = false;
            for (int j = 0; j < Hits.Length; j++)
            {
                FadingObject fadingObject = GetFadingObjectFromHit(Hits[j]);
                if (fadingObject != null && fadingObject == ObjectsBlockingView[i])
                {
                    objectIsBeingHit = true;
                    break;
                }
            }

            if (!objectIsBeingHit)
            {
                if (RunningCoroutines.ContainsKey(ObjectsBlockingView[i]))
                {
                    if (RunningCoroutines[ObjectsBlockingView[i]] != null)
                    {
                        StopCoroutine(RunningCoroutines[ObjectsBlockingView[i]]);
                    }

                    RunningCoroutines.Remove(ObjectsBlockingView[i]);
                }

                RunningCoroutines.Add(ObjectsBlockingView[i], StartCoroutine(FadeObjectIn(ObjectsBlockingView[i])));
                ObjectsBlockingView.RemoveAt(i);
            }
        }
    }

    private IEnumerator FadeObjectOut(FadingObject FadingObject)
    {
        float waitTime = 1f / FadeFPS;
        WaitForSeconds Wait = new WaitForSeconds(waitTime);
        int ticks = 1;

        // for (int i = 0; i < FadingObject.Materials.Count; i++)
        // {
        //     FadingObject.Materials[i]
        //         .SetInt("_SrcBlend",
        //             (int)UnityEngine.Rendering.BlendMode.SrcAlpha); // affects both "Transparent" and "Fade" options
        //     FadingObject.Materials[i]
        //         .SetInt("_DstBlend",
        //             (int)UnityEngine.Rendering.BlendMode
        //                 .OneMinusSrcAlpha); // affects both "Transparent" and "Fade" options
        //     FadingObject.Materials[i].SetInt("_ZWrite", 0); // disable Z writing
        //     if (FadingMode == FadeMode.Fade)
        //     {
        //         FadingObject.Materials[i].EnableKeyword("_ALPHABLEND_ON");
        //     }
        //     else
        //     {
        //         FadingObject.Materials[i].EnableKeyword("_ALPHAPREMULTIPLY_ON");
        //     }
        //
        //     FadingObject.Materials[i].renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        // }
        for (int i = 0; i < FadingObject.Materials.Count; i++)
        {
            SetMaterialTransparentURP(FadingObject.Materials[i]);
        }

        if (FadingObject.Materials[0].HasProperty("_Color"))
        {
            while (FadingObject.Materials[0].color.a > FadedAlpha)
            {
                for (int i = 0; i < FadingObject.Materials.Count; i++)
                {
                    if (FadingObject.Materials[i].HasProperty("_Color"))
                    {
                        FadingObject.Materials[i].color = new Color(
                            FadingObject.Materials[i].color.r,
                            FadingObject.Materials[i].color.g,
                            FadingObject.Materials[i].color.b,
                            Mathf.Lerp(FadingObject.InitialAlpha, FadedAlpha, waitTime * ticks * FadeSpeed)
                        );
                    }
                }

                ticks++;
                yield return Wait;
            }
        }

        if (RunningCoroutines.ContainsKey(FadingObject))
        {
            StopCoroutine(RunningCoroutines[FadingObject]);
            RunningCoroutines.Remove(FadingObject);
        }
    }

    private IEnumerator FadeObjectIn(FadingObject FadingObject)
    {
        float waitTime = 1f / FadeFPS;
        WaitForSeconds Wait = new WaitForSeconds(waitTime);
        int ticks = 1;

        if (FadingObject.Materials[0].HasProperty("_Color"))
        {
            while (FadingObject.Materials[0].color.a < FadingObject.InitialAlpha)
            {
                for (int i = 0; i < FadingObject.Materials.Count; i++)
                {
                    if (FadingObject.Materials[i].HasProperty("_Color"))
                    {
                        FadingObject.Materials[i].color = new Color(
                            FadingObject.Materials[i].color.r,
                            FadingObject.Materials[i].color.g,
                            FadingObject.Materials[i].color.b,
                            Mathf.Lerp(FadedAlpha, FadingObject.InitialAlpha, waitTime * ticks * FadeSpeed)
                        );
                    }
                }

                ticks++;
                yield return Wait;
            }
        }

        // for (int i = 0; i < FadingObject.Materials.Count; i++)
        // {
        //     if (FadingMode == FadeMode.Fade)
        //     {
        //         FadingObject.Materials[i].DisableKeyword("_ALPHABLEND_ON");
        //     }
        //     else
        //     {
        //         FadingObject.Materials[i].DisableKeyword("_ALPHAPREMULTIPLY_ON");
        //     }
        //
        //     FadingObject.Materials[i].SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        //     FadingObject.Materials[i].SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        //     FadingObject.Materials[i].SetInt("_ZWrite", 1); // re-enable Z Writing
        //     FadingObject.Materials[i].renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
        // }
        for (int i = 0; i < FadingObject.Materials.Count; i++)
        {
            SetMaterialOpaqueURP(FadingObject.Materials[i]);
        }

        if (RunningCoroutines.ContainsKey(FadingObject))
        {
            StopCoroutine(RunningCoroutines[FadingObject]);
            RunningCoroutines.Remove(FadingObject);
        }
    }

    private void SetMaterialTransparentURP(Material mat)
    {
        mat.SetFloat("_Surface", 1f); // 0 = Opaque, 1 = Transparent
        mat.SetFloat("_Blend", 0f); // 0 = Alpha, 1 = Premultiply, 2 = Additive, 3 = Multiply
        mat.SetFloat("_ZWrite", 0f);

        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_SURFACE_TYPE_OPAQUE");

        mat.EnableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    private void SetMaterialOpaqueURP(Material mat)
    {
        mat.SetFloat("_Surface", 0f); // Opaque
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_ZWrite", 1f);

        mat.EnableKeyword("_SURFACE_TYPE_OPAQUE");
        mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");

        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
    }

    private FadingObject GetFadingObjectFromHit(RaycastHit Hit)
    {
        return Hit.collider != null ? Hit.collider.GetComponent<FadingObject>() : null;
    }

    private void ClearHits()
    {
        RaycastHit hit = new RaycastHit();
        for (int i = 0; i < Hits.Length; i++)
        {
            Hits[i] = hit;
        }
    }

    public enum FadeMode
    {
        Transparent,
        Fade
    }
}