using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityStandardAssets.ImageEffects;

public class Tracer : PostEffectsBase {

    public bool renderShown = false;
    public bool render = false;
    public bool renderContinuous = false;
    public bool animatedRender = false;
    [Tooltip("Controls how the rays are draw for the non animated mode." +
        " If true, only the rays for the current scanline are drawn. Otherwise all rays are draw for the whole image.")] public bool scanMode = false;
    public int updateRate = 10;
    public float animatedRayStepLength = 0.1f;
    public float maxAnimatedLength = 20f;
    public float animatedPauseTime = 5f;

    [Header("Render Vars")]
    public float ambientCO = 0.1f;
    public float normalBias = 0.005f;
    public int maxDepth = 15;
    public bool shading = true;
    public bool shadows = true;
    public Vector2 RenderResolution;
    public FilterMode renderTexFilterMode = FilterMode.Point;
    public Color bgColor = new Color(0.1f, 0.1f, 0.1f);

    public enum DebugColorType { ColorCoded, MaterialColor };
    [Header("Debug")]
    public DebugColorType debugColorType = DebugColorType.ColorCoded;
    public bool debugMainRays, debugMissedRays, debugShadowRays, debugReflectionRays, debugRefractionRays;

    float deltaTimeMult = 0;

    //an array of lights to hold all the lights in the scene, used in TraceRay
    Light[] lightSources;

    static Tracer tracerInstance;

    struct TracedRay {
        public Color finalColor;
        public Color initialColor;
        public Color shadowedColor;
        public Color reflectedColor;
        public Color refractedColor;
        public bool hit;
        public Vector3 normal;
        public Vector3 hitPoint;
        public float reflectivity;
        public float transparency;
        public float refractiveIndex;
    }

    class AnimatedRay {
        public Ray ray;
        public float length;
        public float maxLength;
        public bool animDone;
        public bool traceDone;
        public bool isSecondary;
        public bool isShadow;
        public Color rayColor;
        public int x;
        public int y;
        public TracedRay trace;
        public AnimatedRay parentRay;

        public AnimatedRay() {
            maxLength = tracerInstance.maxAnimatedLength;
        }

        public void DrawHitPointRay() {
            tracerInstance.DebugRay(DebugRayType.MainRay, ray, trace.hitPoint, rayColor, false);
        }

        public void DrawLengthRay() {
            tracerInstance.DebugRay(DebugRayType.MainRay, ray, ray.origin + ray.direction * length, rayColor, false);
        }
    }


    private Texture2D renderTex;


    private void Awake() {
        tracerInstance = this;
    }

    new private void Start() {
        //we only want this code to be run in play mode.
        if (Application.isPlaying) {
            Application.runInBackground = true;

            renderTex = new Texture2D((int)RenderResolution.x, (int)RenderResolution.y, TextureFormat.RGBAFloat, false);

            if (animatedRender) {
                StartCoroutine(RenderAnimatedCoroutine());
            } else {
                StartCoroutine(RenderCoroutine());
            }
        }
    }

    // Update is called once per frame
    void Update() {
        //return if already playing, we only want this code to be run in play mode.
        if (!Application.isPlaying)
            return;

        HandleInput();

        //if our render tex doesnt exist or if it doesnt match the screen size, create a new one
        //if(!renderTex || renderTex.width != Screen.width || renderTex.height != Screen.height) {
        //    renderTex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBAFloat, false);
        //}

        renderTex.filterMode = renderTexFilterMode;
    }

    private void HandleInput() {
        if (Input.GetKeyDown(KeyCode.R)) {
            render = !render;
        }
    }

    //Handles showing the image to the screen
    private void OnRenderImage(RenderTexture source, RenderTexture destination) {
        if (renderShown && renderTex) {
            Graphics.Blit(renderTex, destination);
        } else {
            Graphics.Blit(source, destination);
        }
    }




    //------------------------------------------------------------------------------
    //-----------------------------RENDERING ROUTINES-------------------------------
    //------------------------------------------------------------------------------

    IEnumerator RenderCoroutine() {
        while (Application.isPlaying) {

            Time.timeScale = 1f;
            yield return null;

            if (render) {

                if (!renderTex || renderTex.width != RenderResolution.x || renderTex.height != RenderResolution.y) {
                    Destroy(renderTex);
                    renderTex = new Texture2D((int)RenderResolution.x, (int)RenderResolution.y, TextureFormat.RGBAFloat, false);
                }

                Time.timeScale = 0f;
                render = false;

                deltaTimeMult = scanMode ? 0f : 0.1f;

                //get all the lights in the scene and put em in an array
                lightSources = FindObjectsOfType<Light>();

                for (int rx = 0; rx < renderTex.width; rx++) {
                    float xScreenCoord = (rx + 0.5f) / renderTex.width * Screen.width;
                    for (int ry = 0; ry < renderTex.height; ry++) {
                        float yScreenCoord = (ry + 0.5f) / renderTex.height * Screen.height;

                        //if render shown is disabled while rendering, stop rendering and restart the render coroutine
                        if (!renderShown) {
                            StartCoroutine(RenderCoroutine());
                            yield break;
                        }

                        //if animated render is turned on, stop and go start the other render method
                        if (animatedRender) {
                            render = true;
                            StartCoroutine(RenderAnimatedCoroutine());
                            yield break;
                        }

                        reflectionDepth = refractionDepth = 0;
                        Ray pixelRay = Camera.main.ScreenPointToRay(new Vector3(xScreenCoord, yScreenCoord));
                        TracedRay tracedPixel = TraceRay(pixelRay, DebugRayType.MainRay);
                        renderTex.SetPixel(rx, ry, tracedPixel.finalColor);

                    }

                    //if rx is is divisible evenly into our update rate, update the texture;
                    if (rx % updateRate == 0) {
                        renderTex.Apply();
                        yield return null;
                    }
                }

                //update the texture and start rendering again if continous is enabled
                renderTex.Apply();
                if (renderContinuous)
                    render = true;
            }
        }
    }

    
    //this animation routine was kinda just slapped together quickly and isnt commented the best

    IEnumerator RenderAnimatedCoroutine() {
        print("start anim");
        while (Application.isPlaying) {

            Time.timeScale = 1f;
            yield return null;

            if (render) {

                Destroy(renderTex);
                renderTex = new Texture2D((int)RenderResolution.x, (int)RenderResolution.y, TextureFormat.RGBAFloat, false);

                Time.timeScale = 0f;
                deltaTimeMult = 0f;
                render = false;

                List<AnimatedRay> animRays = new List<AnimatedRay>();

                //generate animated rays for each pixel in our render
                for (int rx = 0; rx < renderTex.width; rx++) {
                    float xScreenCoord = (rx + 0.5f) / renderTex.width * Screen.width;
                    for (int ry = 0; ry < renderTex.height; ry++) {
                        float yScreenCoord = (ry + 0.5f) / renderTex.height * Screen.height;
                        Ray pixelRay = Camera.main.ScreenPointToRay(new Vector3(xScreenCoord, yScreenCoord));

                        animRays.Add(new AnimatedRay() {
                            ray = pixelRay,
                            x = rx,
                            y = ry,
                            rayColor = Color.white
                        });
                    }
                }

                //get all the lights in the scene and put em in an array
                lightSources = FindObjectsOfType<Light>();

                bool done = true;
                int steps = 0;

                int animUpdateRate = renderTex.width * renderTex.height;


                do {
                    done = true;

                    //loop over all the anim rays and update/draw them
                    for (int i = 0; i < animRays.Count; i++) {

                        //if steps is is divisible evenly into our update rate, update the texture;
                        if (steps++ % animUpdateRate == 0) {
                            renderTex.Apply();
                            //yield return null;
                        }

                        //if render shown is disabled while rendering, stop rendering and restart the render coroutine
                        if (!renderShown) {
                            StartCoroutine(RenderAnimatedCoroutine());
                            yield break;
                        }

                        //if animated render is disabled, stop and go start the other render method
                        if (!animatedRender) {
                            render = true;
                            StartCoroutine(RenderCoroutine());
                            yield break;
                        }

                        AnimatedRay animRay = animRays[i];


                        if (animRay.animDone) {
                            //if this ray is finished animating just draw its full path and continue
                            animRay.DrawHitPointRay();
                            continue;

                        } else if (animRay.length > animRay.maxLength) {

                            //when the length is greater than its maxlength then the animation of the ray has completed

                            TracedRay tracedPixel = animRay.trace;

                            animRay.animDone = true;
                            animRay.trace.hitPoint = animRay.ray.origin + animRay.ray.direction * animRay.maxLength;




                            if (animRay.isShadow) {

                                //if this ray is a shadow ray, and it hits something, then the ray is in shadow and should change the color to match
                                if (animRay.trace.hit) {
                                    animRay.rayColor = Color.black;
                                }

                                //add anim ray for transparent shadows
                                if (tracedPixel.transparency > 0) {
                                    animRays.Add(new AnimatedRay() {
                                        isShadow = true,
                                        parentRay = animRay.parentRay,
                                        ray = GetRefractionRay(animRay.ray, tracedPixel.hitPoint, tracedPixel.normal, tracedPixel.refractiveIndex),
                                        x = animRay.x, y = animRay.y,
                                    });
                                }

                            } else {

                                animRay.rayColor = tracedPixel.initialColor;

                                if (debugShadowRays) {
                                    //add animated shadow rays for each light source if we are drawing shadow rays
                                    foreach (Light lite in lightSources) {
                                        Vector3 hitPoint = AddNormalBias(tracedPixel.hitPoint, tracedPixel.normal);
                                        Vector3 pointToLight = lite.transform.position - hitPoint;
                                        float distToLight = pointToLight.magnitude;

                                        animRays.Add(new AnimatedRay() {
                                            isShadow = true,
                                            parentRay = animRay,
                                            ray = new Ray(hitPoint, pointToLight),
                                            maxLength = distToLight,
                                            rayColor = lite.color,
                                            x = animRay.x, y = animRay.y,
                                        });
                                    }
                                }

                                bool secondaries = false;

                                if (debugReflectionRays && tracedPixel.reflectivity > 0) {
                                    secondaries = true;

                                    //add animated reflection ray if we are drawing reflection rays
                                    animRays.Add(new AnimatedRay() {
                                        isSecondary = true,
                                        parentRay = animRay,
                                        ray = GetReflectionRay(animRay.ray.direction, tracedPixel.hitPoint, tracedPixel.normal),
                                        rayColor = Color.gray,
                                        x = animRay.x, y = animRay.y,
                                    });
                                }

                                if (debugRefractionRays && tracedPixel.transparency > 0) {
                                    secondaries = true;

                                    //add animated refraction ray if we are drawing refraction rays
                                    animRays.Add(new AnimatedRay() {
                                        isSecondary = true,
                                        parentRay = animRay,
                                        ray = GetRefractionRay(animRay.ray, tracedPixel.hitPoint, tracedPixel.normal, tracedPixel.refractiveIndex),
                                        rayColor = Color.gray,
                                        x = animRay.x, y = animRay.y,
                                    });
                                }

                                if (!animRay.isSecondary && !animRay.isShadow) {
                                    //this is basically only triggered when the ray is a primary ray
                                    //sets the pixel of the primary ray to the initial color
                                    //the initial color doesnt contain reflections or refractions
                                    renderTex.SetPixel(animRay.x, animRay.y, tracedPixel.initialColor);

                                } else if (!secondaries && animRay.isSecondary) {

                                    //if we were animating a secondary ray, and it has not spawned any more secondary rays
                                    //update its pixel with the final color
                                    //this is how we get the sortve animated effect of initial color first, and reflections added as the reflection rays finish
                                    renderTex.SetPixel(animRay.x, animRay.y, animRay.parentRay.trace.finalColor);
                                }
                            }


                        } else if (animRay.traceDone) {

                            //if the trace for the ray is done but the animation isnt, update the animation ray length and draw it
                            animRay.length += animatedRayStepLength;

                            animRay.DrawLengthRay();

                            //if any ray is still animating in some way then it sets done to false so that it knows to keep running the loop for all rays to animate.
                            done = false;
                        }



                        if (!animRay.traceDone) {
                            //this bit is the bit that actually does the trace for a ray
                            reflectionDepth = refractionDepth = 0;
                            TracedRay tracedPixel = TraceRay(animRay.ray, DebugRayType.MainRay, animRay.maxLength);
                            animRay.trace = tracedPixel;

                            if (tracedPixel.hit) {
                                animRay.maxLength = Vector3.Distance(animRay.ray.origin, tracedPixel.hitPoint);

                            } else {

                                if (!animRay.isShadow && !debugMissedRays) {
                                    //remove non shadow rays if they miss and we arent showing missed rays
                                    animRays.RemoveAt(i--);
                                }
                            }

                            animRay.traceDone = true;
                            done = false;
                        }




                    }

                    yield return null;


                } while (!done);

                print("anim done");

                renderTex.Apply();

                //this is a really weird bit of nonsense to do with how debug.drawray works
                deltaTimeMult = 0.9f;
                foreach (AnimatedRay animRay in animRays) {
                    animRay.DrawHitPointRay();
                }
                deltaTimeMult = 0;

                yield return new WaitForSecondsRealtime(animatedPauseTime);

                if (renderContinuous)
                    render = true;

            }
        }
    }

    private enum DebugRayType { MainRay, MainRayMiss, ShadowRay, ShadowRayBlocked, ReflectedRay, RefractedRay };
    private Dictionary<DebugRayType, Color> RayColorDictionary = new Dictionary<DebugRayType, Color> {
        { DebugRayType.MainRay, Color.cyan },
        { DebugRayType.MainRayMiss, Color.gray },
        { DebugRayType.ShadowRay, Color.yellow },
        { DebugRayType.ShadowRayBlocked, Color.black },
        { DebugRayType.ReflectedRay, Color.green },
        { DebugRayType.RefractedRay, Color.magenta }
    };

    private float debugRayDuration => Time.unscaledDeltaTime * deltaTimeMult;

    void DebugRay(DebugRayType type, Ray ray, Vector3 hitPoint, Color color, bool checkAnim = true) {
        if (checkAnim && animatedRender) return;

        Color rayColor = bgColor;
        if (debugColorType == DebugColorType.ColorCoded)
            rayColor = RayColorDictionary[type];
        else
            rayColor = color;

        if (hitPoint == Vector3.zero) {
            Debug.DrawRay(ray.origin, ray.direction * 9999f, rayColor, debugRayDuration);
        } else {
            Debug.DrawLine(ray.origin, hitPoint, rayColor, debugRayDuration);
        }
    }



    //------------------------------------------------------------------------------
    //---------------------------RAY TRACING FUNCTIONS------------------------------
    //------------------------------------------------------------------------------

    //i copied this from my other project so it isnt commented very well and has more features than is necessary for this demo
    //i.e. its not as simple as it could be.

    float GetShadowTexOpacity(RaycastHit sHit) {
        Material sRendMat = sHit.collider.GetComponent<Renderer>().sharedMaterial;
        Vector2 sTexHit = Vector3.Scale(sHit.textureCoord, sRendMat.mainTextureScale);
        Texture2D sTex = (Texture2D)sRendMat.mainTexture;
        float sTexOpac = 1f; if (sTex)
            sTexOpac = sTex.GetPixelBilinear(sTexHit.x, sTexHit.y).a;
        return sRendMat.color.a * sTexOpac;
    }

    float RecursiveShadowRay(Ray ray, float inShadow, Vector3 lightPos) {
        float actShadow = inShadow;
        float diffuseCO = 1f - ambientCO;
        float dist = Vector3.Distance(ray.origin, lightPos);
        RaycastHit tHit;
        if (Physics.Raycast(ray, out tHit, dist)) {
            float trans = GetShadowTexOpacity(tHit);
            if (trans < 1f) {
                actShadow *= RecursiveShadowRay(new Ray(tHit.point + ray.direction * normalBias, ray.direction), 1f - diffuseCO * trans, lightPos);
            } else {
                actShadow *= 1f - diffuseCO * trans;
            }
        }
        return actShadow;
    }

    private int reflectionDepth = 0;
    private int refractionDepth = 0;

    TracedRay TraceRay(Ray oRay, DebugRayType rayType, float maxDist = float.PositiveInfinity) {

        RaycastHit hit;
        if (Physics.Raycast(oRay, out hit, maxDist)) {
            Material rendMat = hit.collider.GetComponent<Renderer>().sharedMaterial;
            float reflectance = rendMat.GetFloat("_Metallic");
            float refInd = rendMat.GetFloat("_Glossiness");
            float opacity = rendMat.color.a;
            float diffuseCO = 1f - ambientCO;

            Texture2D tex = rendMat.mainTexture as Texture2D;
            Vector2 texCoord = Vector3.Scale(hit.textureCoord, rendMat.mainTextureScale);
            texCoord += rendMat.mainTextureOffset;
            Color texColor = tex ? tex.GetPixelBilinear(texCoord.x, texCoord.y) * rendMat.color : rendMat.color;
            float texOpacity = tex ? texColor.a : 1f;
            opacity *= texOpacity;
            float transparency = 1f - opacity;

            Color pointColor = texColor * ambientCO;
            Color unShadowedColor = pointColor;

            foreach (Light lite in lightSources) {
                float distToLight = Vector3.Distance(lite.transform.position, hit.point);
                Vector3 lightPos = lite.transform.position;
                Vector3 pointToLight = GetDirectionTo(hit.point, lightPos);
                Vector3 lightToPoint = -pointToLight;
                float shade = Mathf.Clamp01(Vector3.Dot(pointToLight, hit.normal));
                float shadeValue = (ambientCO + diffuseCO * shade);
                float lightPower = lite.intensity * shade;
                float lightFalloff = Mathf.Clamp01((lite.range - distToLight) / lite.range);

                if (distToLight < lite.range) {
                    Color tmpColor = texColor * lite.color;

                    if (shading)
                        tmpColor *= shadeValue;

                    unShadowedColor += tmpColor * lightPower * lightFalloff;

                    if (shadows && lite.shadows != LightShadows.None) {
                        Ray shadowRay = new Ray(AddNormalBias(hit.point, hit.normal), pointToLight);
                        RaycastHit shadowHit;
                        if (Physics.Raycast(shadowRay, out shadowHit, distToLight)) {
                            //light is blocked, in shadow
                            float trans = GetShadowTexOpacity(shadowHit);
                            if (trans < 1f) {
                                //handle shadows on transparent objects
                                tmpColor *= Mathf.Clamp(RecursiveShadowRay(new Ray(AddNormalBias(shadowHit.point, shadowRay.direction), shadowRay.direction), 1f - diffuseCO * trans, lightPos), ambientCO, 1f);
                            } else {
                                tmpColor *= 1f - diffuseCO; //hard shadows
                            }
                            if (debugShadowRays)
                                DebugRay(DebugRayType.ShadowRayBlocked, shadowRay, shadowHit.point, tmpColor);
                        } else {
                            //light not blocked, not in shadow
                            if (debugShadowRays)
                                DebugRay(DebugRayType.ShadowRay, shadowRay, lite.transform.position, lite.color);

                            tmpColor *= lightPower * lightFalloff;
                        }
                    }

                    pointColor += tmpColor;
                }

            }

            if (debugMainRays) {
                DebugRay(rayType, oRay, hit.point, pointColor);
            }


            //most of this is only needed because of the animation process
            TracedRay hitRay = new TracedRay() {
                hit = true,
                initialColor = pointColor,
                shadowedColor = pointColor,
                hitPoint = hit.point,
                normal = hit.normal,
                reflectivity = reflectance,
                transparency = transparency,
                refractiveIndex = refInd
            };

            if (reflectance > 0 || opacity < 1) {
                TracedRay reflectRay, refractRay;
                reflectRay = refractRay = new TracedRay();
                if (reflectance > 0 && reflectionDepth < maxDepth) {
                    reflectionDepth++;
                    reflectRay = TraceRay(GetReflectionRay(oRay.direction, hit.point, hit.normal), DebugRayType.ReflectedRay);
                    reflectRay.finalColor *= reflectance;
                }
                if (opacity < 1f && refractionDepth < maxDepth) {
                    refractionDepth++;
                    refractRay = TraceRay(GetRefractionRay(oRay, hit.point, hit.normal, refInd), DebugRayType.RefractedRay);
                    refractRay.finalColor *= transparency;
                    pointColor *= opacity;
                }

                hitRay.reflectedColor = reflectRay.finalColor;
                hitRay.refractedColor = refractRay.finalColor;
                pointColor += reflectRay.finalColor + refractRay.finalColor;
            }

            hitRay.finalColor = pointColor;


            return hitRay;

        } else {
            if (debugMissedRays) DebugRay(DebugRayType.MainRayMiss, oRay, Vector3.zero, bgColor);
            TracedRay tracedRay = new TracedRay() {
                hit = false,
                finalColor = bgColor
            };
            return tracedRay;
        }
    }

    Vector3 GetDirectionTo(Vector3 origin, Vector3 destination) {
        return destination - origin;
    }

    Vector3 AddNormalBias(Vector3 inPoint, Vector3 inNormal) {
        return inPoint + inNormal * normalBias;
    }

    Ray GetReflectionRay(Vector3 dir, Vector3 point, Vector3 normal) {
        return new Ray(AddNormalBias(point, normal), Vector3.Reflect(dir, normal));
    }

    //dont worry too much about the maths here
    Ray GetRefractionRay(Ray oRay, Vector3 point, Vector3 normal, float refractiveIndex) {
        float n = 1f / (refractiveIndex + 1f);
        float c1 = -Vector3.Dot(normal, oRay.direction);
        float c2 = Mathf.Sqrt(1f - Mathf.Pow(n, 2) * (1f - Mathf.Pow(c1, 2)));
        return new Ray(AddNormalBias(point, -normal), (n * oRay.direction) + (n * c1 - c2) * normal);
    }
    



}
