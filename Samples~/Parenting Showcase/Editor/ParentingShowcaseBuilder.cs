using System.Collections.Generic;
using TMPro;
using Unity.Scenes;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using DetachTrack = BovineLabs.Timeline.Parenting.Authoring.TemporaryDetachTrack;
using DetachClip = BovineLabs.Timeline.Parenting.Authoring.TemporaryDetachClip;
using PositionTrack = BovineLabs.Timeline.Transform.Authoring.TransformPositionTrack;
using PositionClip = BovineLabs.Timeline.Transform.Authoring.PositionClip;
using PositionType = BovineLabs.Timeline.Transform.Authoring.PositionType;
using TimelineBeginAuthoring = BovineLabs.Timeline.Core.Authoring.TimelineBeginAuthoring;
using TimelineBeginMode = BovineLabs.Timeline.Core.Authoring.TimelineBeginMode;

public static class ParentingShowcaseBuilder
{
    private const string SampleFolder = "Assets/Samples/ParentingShowcase";
    private const string TimelineFolder = SampleFolder + "/Timelines";
    private const string ParentPath = SampleFolder + "/ParentingShowcase.unity";
    private const string SubPath = SampleFolder + "/ParentingShowcase_Sub.unity";

    private static readonly Color MomentaryColor = new Color(0.10f, 0.65f, 0.65f);
    private static readonly Color SequentialColor = new Color(0.85f, 0.55f, 0.20f);
    private static readonly Color LoopedColor = new Color(0.80f, 0.30f, 0.85f);
    private static readonly Color ParentColor = new Color(0.95f, 0.25f, 0.25f);
    private static readonly Color ChildColor = new Color(0.30f, 0.70f, 1.00f);
    private static readonly Color ControlChildColor = new Color(0.35f, 0.85f, 0.45f);
    private static readonly Color PadColor = new Color(0.22f, 0.24f, 0.29f);
    private static readonly Color BannerColor = new Color(0.06f, 0.08f, 0.12f);

    private const float MomentaryX = -16f;
    private const float SequentialX = 0f;
    private const float LoopedX = 16f;
    private const float RowStep = 9f;
    private const float BaseY = 1.2f;
    private const float ChildLocalY = 2.2f;

    private static readonly Vector3 CameraPos = new Vector3(0f, 16f, -34f);

    private static Scene activeSub;

    private enum BindKind
    {
        ParentTransform,
        ChildGameObject,
    }

    private sealed class TrackBind
    {
        public string TrackName;
        public string TargetName;
        public BindKind Kind;
    }

    private sealed class CellWire
    {
        public string DirectorName;
        public string TimelinePath;
        public List<TrackBind> Binds;
    }

    private static readonly List<CellWire> Wires = new List<CellWire>();

    private sealed class CaptionData
    {
        public string Title;
        public string Usage;
        public Vector3 CellPos;
        public Color Color;
    }

    private static readonly List<CaptionData> Captions = new List<CaptionData>();

    [MenuItem("Showcase/Build Parenting")]
    public static void Build()
    {
        Wires.Clear();
        Captions.Clear();
        EnsureFolders();
        ResetAssets();

        var parent = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(parent, ParentPath);
        var sub = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        EditorSceneManager.SetActiveScene(sub);
        activeSub = sub;

        BuildPads();
        BuildMomentaryColumn();
        BuildSequentialColumn();
        BuildLoopedColumn();

        EditorSceneManager.SaveScene(sub, SubPath);
        EditorSceneManager.SetActiveScene(parent);
        EditorSceneManager.CloseScene(sub, true);

        sub = EditorSceneManager.OpenScene(SubPath, OpenSceneMode.Additive);
        EditorSceneManager.SetActiveScene(sub);
        activeSub = sub;

        foreach (var w in Wires)
        {
            WireCell(w);
        }

        EditorSceneManager.MarkSceneDirty(sub);
        EditorSceneManager.SaveScene(sub);

        EditorSceneManager.SetActiveScene(parent);
        BuildParent();
        EditorSceneManager.SaveScene(parent);

        EditorSceneManager.CloseScene(sub, true);
        EditorSceneManager.OpenScene(ParentPath, OpenSceneMode.Single);

        Debug.Log("ParentingShowcase: built grid at " + ParentPath + " | directors=" + Wires.Count);
    }

    // ============================================================
    //  Rig: a MOVING parent (own PositionTrack orbit) + a CHILD
    //  cube parented under it. The detach director binds the CHILD
    //  GameObject. While a detach clip is active the child loses its
    //  Parent and holds its world pose (no longer follows the moving
    //  parent); at clip end it re-parents and snaps back onto the
    //  parent's offset and resumes following.
    // ============================================================

    private sealed class Rig
    {
        public GameObject Parent;
        public GameObject Child;
    }

    private static Rig BuildRig(string cell, float x, float z, double loopLen, Color childColor, bool resetTrack)
    {
        var parentHome = new Vector3(x, BaseY, z);
        var parentGo = MakeCube(cell + "_Parent", parentHome, new Vector3(1.3f, 1.3f, 1.3f), ParentColor);

        var child = MakeCube(cell + "_Child", parentHome + new Vector3(0f, ChildLocalY, 0f), new Vector3(0.8f, 0.8f, 0.8f), childColor);
        child.transform.SetParent(parentGo.transform, true);

        DriveParentOrbit(cell, parentGo, parentHome, loopLen, resetTrack);

        return new Rig { Parent = parentGo, Child = child };
    }

    private static void DriveParentOrbit(string cell, GameObject parentGo, Vector3 home, double loopLen, bool resetTrack)
    {
        var path = TimelineFolder + "/" + cell + "_ParentOrbit.playable";
        var timeline = NewTimeline(path);
        var track = timeline.CreateTrack<PositionTrack>(null, "Position");
        track.ResetPositionOnDeactivate = true;

        var q = loopLen / 4.0;
        var a = AddWorldPos(track, 0.0, q, "to +X", home + new Vector3(4.0f, 0f, 0f));
        var b = AddWorldPos(track, q, q, "to +Z", home + new Vector3(0f, 0f, 4.0f));
        var c = AddWorldPos(track, q * 2, q, "to -X", home + new Vector3(-4.0f, 0f, 0f));
        var d = AddWorldPos(track, q * 3, q, "home", home);
        Blend(a, b, c, d);
        FixDuration(timeline);
        Dirty(timeline, track);
        AssetDatabase.SaveAssets();

        var dirName = cell + "_ParentDir";
        MakeDirector(dirName);
        Wires.Add(new CellWire
        {
            DirectorName = dirName,
            TimelinePath = path,
            Binds = new List<TrackBind>
            {
                new TrackBind { TrackName = "Position", TargetName = parentGo.name, Kind = BindKind.ParentTransform },
            },
        });
    }

    private static TimelineClip AddWorldPos(PositionTrack t, double start, double dur, string name, Vector3 world)
    {
        var c = AddClip<PositionClip>(t, start, dur, name);
        var a = (PositionClip)c.asset;
        a.Type = PositionType.World;
        a.Position = world;
        Dirty(c.asset);
        return c;
    }

    // ============================================================
    //  COLUMN A — MOMENTARY (Pattern A): one detach clip = the free
    //  window. Reattach is automatic at clip end.
    // ============================================================

    private static void BuildMomentaryColumn()
    {
        MomentaryCell(0, 8.0, new[] { new Vector2(2.0f, 4.0f) }, "Pop loose [2,6]",
            "Pattern A: ONE TemporaryDetachClip over [2,6] on an 8s loop. ENTER (rising edge) records Parent + local pose, freezes the child's world pose (no pop) and removes Parent -> child stops following the orbiting red parent and hangs in place. EXIT (falling edge) restores the captured LOCAL pose and re-adds Parent -> child snaps back onto the parent and resumes following.");

        MomentaryCell(1, 8.0, new[] { new Vector2(1.0f, 6.0f) }, "Long detach [1,7]",
            "Pattern A, longer window [1,7]: the child stays free for most of the loop while the parent completes its orbit, then reattaches at 7 and rides home. Shows the detach holds world pose for an arbitrary span; reattach restores the local offset, not the live world position.");
    }

    private static void MomentaryCell(int row, double loopLen, Vector2[] windows, string label, string usage)
    {
        var z = row * RowStep;
        var cell = "Mom" + row;
        var rig = BuildRig(cell, MomentaryX, z, loopLen, ChildColor, resetTrack: true);
        BuildDetach(cell, MomentaryX, z, loopLen, windows, rig, label, usage, MomentaryColor, resetOnDeactivate: true);
    }

    // ============================================================
    //  COLUMN B — SEQUENTIAL (Pattern B): two NON-overlapping detach
    //  clips = two separate free windows; the gap between = attached.
    // ============================================================

    private static void BuildSequentialColumn()
    {
        SequentialCell(0, 10.0, new[] { new Vector2(1.0f, 2.5f), new Vector2(6.0f, 2.5f) }, "Two releases",
            "Pattern B: TWO non-overlapping clips [1,3.5] and [6,8.5] on a 10s loop. Each window detaches fresh (re-captures parent+pose) and reattaches at its own end. Between the windows the child is parented and rides the orbiting parent. NO Blending cap -> clips must never overlap.");

        SequentialCell(1, 10.0, new[] { new Vector2(0.5f, 1.5f), new Vector2(3.0f, 1.5f), new Vector2(5.5f, 1.5f) }, "Three pulses",
            "Pattern B taken to three short sequential windows ([0.5,2],[3,4.5],[5.5,7]): detach / reattach / detach / reattach repeatedly within one loop. Each release independently captures the live parent so reattach always seats correctly even as the parent has moved.");
    }

    private static void SequentialCell(int row, double loopLen, Vector2[] windows, string label, string usage)
    {
        var z = row * RowStep;
        var cell = "Seq" + row;
        var rig = BuildRig(cell, SequentialX, z, loopLen, ChildColor, resetTrack: true);
        BuildDetach(cell, SequentialX, z, loopLen, windows, rig, label, usage, SequentialColor, resetOnDeactivate: true);
    }

    // ============================================================
    //  COLUMN C — LOOPED FLICKER (Pattern C) + a CONTROL row that
    //  proves the baseline (no detach -> always follows) and the
    //  resetOnDeactivate=false variant (documented no-op for this
    //  track).
    // ============================================================

    private static void BuildLoopedColumn()
    {
        // Row 0 — Pattern C: a single Looping clip pulsing detach each loop.
        {
            var z = 0 * RowStep;
            var cell = "Loop0";
            var loopLen = 3.0;
            var rig = BuildRig(cell, LoopedX, z, loopLen, ChildColor, resetTrack: true);

            var path = TimelineFolder + "/" + cell + "_Detach.playable";
            var timeline = NewTimeline(path);
            var track = timeline.CreateTrack<DetachTrack>(null, "Detach");
            SetResetOnDeactivate(track, true);
            var clip = AddClip<DetachClip>(track, 0.5, 2.0, "flicker");
            Dirty(clip.asset);
            FixDuration(timeline);
            Dirty(timeline, track);
            AssetDatabase.SaveAssets();

            FinishDetachWire(cell, LoopedX, z, path, rig,
                "Looped flicker",
                "Pattern C: one clip with ClipCaps.Looping pulsing detach on a short 3s loop. Each loop re-runs the ENTER capture and EXIT restore -> the child repeatedly pops loose and snaps back. Every loop pays one end-of-frame ECB structural change (use sparingly).",
                LoopedColor);
        }

        // Row 1 — CONTROL: no detach track at all -> the child simply rides the parent forever.
        {
            var z = 1 * RowStep;
            var cell = "Loop1";
            var loopLen = 8.0;
            BuildRig(cell, LoopedX, z, loopLen, ControlChildColor, resetTrack: true);

            Captions.Add(new CaptionData
            {
                Title = "Control (no detach)",
                Usage = "Baseline with NO TemporaryDetachTrack: the green child is a plain transform-child of the orbiting parent and follows it for the entire loop. This is the 'attached' reference against which every detach cell's free window reads -> the contrast that proves the track fired.",
                CellPos = new Vector3(LoopedX, CaptionTopY(z), z),
                Color = ControlChildColor,
            });
        }
    }

    // ============================================================
    //  detach authoring + wiring plumbing
    // ============================================================

    private static void BuildDetach(string cell, float x, float z, double loopLen, Vector2[] windows, Rig rig,
        string label, string usage, Color color, bool resetOnDeactivate)
    {
        var path = TimelineFolder + "/" + cell + "_Detach.playable";
        var timeline = NewTimeline(path);
        var track = timeline.CreateTrack<DetachTrack>(null, "Detach");
        SetResetOnDeactivate(track, resetOnDeactivate);

        foreach (var w in windows)
        {
            var clip = AddClip<DetachClip>(track, w.x, w.y, "detach");
            Dirty(clip.asset);
        }

        timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
        timeline.fixedDuration = loopLen;
        Dirty(timeline, track);
        AssetDatabase.SaveAssets();

        FinishDetachWire(cell, x, z, path, rig, label, usage, color);
    }

    private static void FinishDetachWire(string cell, float x, float z, string path, Rig rig, string label, string usage, Color color)
    {
        var dirName = cell + "_DetachDir";
        MakeDirector(dirName);
        Wires.Add(new CellWire
        {
            DirectorName = dirName,
            TimelinePath = path,
            Binds = new List<TrackBind>
            {
                new TrackBind { TrackName = "Detach", TargetName = rig.Child.name, Kind = BindKind.ChildGameObject },
            },
        });

        Captions.Add(new CaptionData
        {
            Title = label,
            Usage = usage,
            CellPos = new Vector3(x, CaptionTopY(z), z),
            Color = color,
        });
    }

    private static void SetResetOnDeactivate(TrackAsset track, bool value)
    {
        var so = new SerializedObject(track);
        var prop = so.FindProperty("resetOnDeactivate");
        if (prop != null)
        {
            prop.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void WireCell(CellWire w)
    {
        var director = GameObject.Find(w.DirectorName).GetComponent<PlayableDirector>();
        var timeline = AssetDatabase.LoadAssetAtPath<TimelineAsset>(w.TimelinePath);
        director.playableAsset = timeline;

        foreach (var track in timeline.GetOutputTracks())
        {
            var bind = FindBind(w, track.name);
            if (bind == null)
            {
                continue;
            }

            var go = GameObject.Find(bind.TargetName);
            if (bind.Kind == BindKind.ParentTransform)
            {
                director.SetGenericBinding(track, go.transform);
            }
            else
            {
                director.SetGenericBinding(track, go);
            }
        }

        EditorUtility.SetDirty(director);
    }

    private static TrackBind FindBind(CellWire w, string trackName)
    {
        foreach (var b in w.Binds)
        {
            if (b.TrackName == trackName)
            {
                return b;
            }
        }

        return null;
    }

    private static PlayableDirector MakeDirector(string name)
    {
        var go = new GameObject(name);
        SceneManager.MoveGameObjectToScene(go, activeSub);
        var director = go.AddComponent<PlayableDirector>();
        director.playOnAwake = true;
        director.extrapolationMode = DirectorWrapMode.Loop;
        var begin = go.AddComponent<TimelineBeginAuthoring>();
        begin.Mode = TimelineBeginMode.OnLoad;
        begin.DelaySeconds = 0f;
        return director;
    }

    private static TimelineAsset NewTimeline(string path)
    {
        var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
        AssetDatabase.CreateAsset(timeline, path);
        return timeline;
    }

    private static TimelineClip AddClip<T>(TrackAsset track, double start, double duration, string name) where T : PlayableAsset
    {
        var clip = track.CreateClip<T>();
        clip.start = start;
        clip.duration = duration;
        clip.displayName = name;
        return clip;
    }

    private static void Blend(params TimelineClip[] clips)
    {
        foreach (var c in clips)
        {
            c.blendInDuration = 0.4;
        }
    }

    private static void FixDuration(TimelineAsset timeline)
    {
        var end = 0.0;
        foreach (var track in timeline.GetOutputTracks())
        {
            foreach (var clip in track.GetClips())
            {
                var clipEnd = clip.start + clip.duration;
                if (clipEnd > end)
                {
                    end = clipEnd;
                }
            }
        }

        timeline.durationMode = TimelineAsset.DurationMode.FixedLength;
        timeline.fixedDuration = end;
    }

    // ============================================================
    //  primitives
    // ============================================================

    private static GameObject MakeCube(string name, Vector3 pos, Vector3 scale, Color color)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position = pos;
        go.transform.localScale = scale;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(name, color);
        SceneManager.MoveGameObjectToScene(go, activeSub);
        return go;
    }

    private static GameObject MakePad(string name, Vector3 pos, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.position = pos;
        go.transform.localScale = size;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(name, PadColor);
        SceneManager.MoveGameObjectToScene(go, activeSub);
        return go;
    }

    private static void BuildPads()
    {
        float[] xs = { MomentaryX, SequentialX, LoopedX };
        string[] names = { "Momentary", "Sequential", "Looped" };
        for (var i = 0; i < xs.Length; i++)
        {
            MakePad(names[i] + "_Pad", new Vector3(xs[i], 0.05f, RowStep * 0.5f), new Vector3(11.0f, 0.12f, 24f));
        }
    }

    private static Material MakeMaterial(string name, Color color)
    {
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        var mat = new Material(shader) { name = name + "_Mat" };
        mat.color = color;
        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", color);
        }

        return mat;
    }

    // ============================================================
    //  parent scene: camera, labels, subscene
    // ============================================================

    private static void BuildParent()
    {
        FrameCamera();
        RenderSettings.fog = false;

        MakeBanner("Title_Banner", new Vector3(0f, 14.4f, 0f), new Vector3(46f, 3.4f, 0.1f));
        MakeWorldLabel("Title", "PARENTING TIMELINE GRID", new Vector3(0f, 14.8f, -0.4f), 46f, Color.white, 5.0f, TextAlignmentOptions.Center);
        MakeWorldLabel("Subtitle", "TemporaryDetachTrack — temporary unparent + auto reattach   ·   com.bovinelabs.timeline.parenting", new Vector3(0f, 13.5f, -0.4f), 46f, new Color(0.85f, 0.9f, 1f), 1.9f, TextAlignmentOptions.Center);

        MakeColumnHeader("Mom_Header", "MOMENTARY  (1 clip)", MomentaryX, MomentaryColor);
        MakeColumnHeader("Seq_Header", "SEQUENTIAL  (n clips)", SequentialX, SequentialColor);
        MakeColumnHeader("Loop_Header", "LOOPED + CONTROL", LoopedX, LoopedColor);

        foreach (var cap in Captions)
        {
            MakeCaption(cap.Title, cap.Usage, cap.CellPos, cap.Color);
        }

        MakeBanner("Usage_Banner", new Vector3(0f, 0.7f, -8.5f), new Vector3(56f, 2.2f, 0.1f));
        MakeWorldLabel("Usage",
            "Red cubes = MOVING parents (each on its own world-space orbit timeline). Blue/green cubes = CHILDREN parented under them. A TemporaryDetach director bound to the CHILD GameObject pops it off its runtime Parent for the clip window (child holds world pose, stops following) then re-adds Parent at clip end (child snaps back onto the parent's local offset and resumes following). FixedLength + Loop.",
            new Vector3(0f, 0.7f, -8.8f), 54f, new Color(0.96f, 0.97f, 1f), 1.5f, TextAlignmentOptions.Center);

        var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(SubPath);
        if (sceneAsset == null)
        {
            Debug.LogError("ParentingShowcase: sub-scene asset missing at " + SubPath);
            return;
        }

        var subSceneGo = new GameObject("Showcase SubScene");
        var subScene = subSceneGo.AddComponent<SubScene>();
        subScene.SceneAsset = sceneAsset;
        subScene.AutoLoadScene = true;
        EditorUtility.SetDirty(subScene);
    }

    private static void MakeColumnHeader(string name, string text, float x, Color color)
    {
        var pos = new Vector3(x, 4.4f, -5.0f);
        MakeBanner(name + "_Banner", pos + new Vector3(0f, 0f, 0.08f), new Vector3(10.4f, 1.3f, 0.1f));
        MakeWorldLabel(name, "<b>" + text + "</b>", pos, 10.2f, color, 2.4f, TextAlignmentOptions.Center);
    }

    private static float CaptionTopY(float z)
    {
        return 4.8f + z * 0.14f;
    }

    private static void MakeCaption(string title, string usage, Vector3 cellPos, Color color)
    {
        var z = cellPos.z;
        var y = cellPos.y;
        MakeBanner("CapBanner_" + title + "_" + z, new Vector3(cellPos.x, y, z + 0.06f), new Vector3(10.4f, 2.2f, 0.05f));
        MakeWorldLabel("Cap_" + title + "_" + z, "<b>" + title + "</b>", new Vector3(cellPos.x, y + 0.6f, z), 10.2f, color, 2.4f, TextAlignmentOptions.Center);
        MakeWorldLabel("Use_" + title + "_" + z, usage, new Vector3(cellPos.x, y - 0.45f, z), 10.2f, new Color(0.95f, 0.96f, 1f), 1.15f, TextAlignmentOptions.Center);
    }

    private static void FrameCamera()
    {
        var required = GameObject.Find("Required In Scene");
        if (required == null)
        {
            return;
        }

        var camTransform = required.transform.Find("Main Camera");
        if (camTransform == null)
        {
            return;
        }

        camTransform.position = CameraPos;
        camTransform.rotation = Quaternion.Euler(20f, 0f, 0f);
        var cam = camTransform.GetComponent<Camera>();
        if (cam != null)
        {
            cam.fieldOfView = 60f;
            cam.farClipPlane = 400f;
            EditorUtility.SetDirty(cam);
        }

        EditorUtility.SetDirty(camTransform);
    }

    private static void MakeBanner(string name, Vector3 pos, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.position = pos;
        go.transform.rotation = Quaternion.LookRotation(pos - CameraPos, Vector3.up);
        go.transform.localScale = size;
        go.GetComponent<MeshRenderer>().sharedMaterial = MakeMaterial(name, BannerColor);
    }

    private static void MakeWorldLabel(string name, string text, Vector3 pos, float width, Color color, float fontSize, TextAlignmentOptions alignment)
    {
        var holder = new GameObject(name);
        holder.transform.position = pos;
        holder.transform.rotation = Quaternion.LookRotation(pos - CameraPos, Vector3.up);

        var go = new GameObject("Text");
        go.transform.SetParent(holder.transform, false);
        var tmp = go.AddComponent<TextMeshPro>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.rectTransform.sizeDelta = new Vector2(width, 4f);
        tmp.rectTransform.localPosition = Vector3.zero;
        tmp.fontStyle = FontStyles.Bold;
    }

    private static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Samples"))
        {
            AssetDatabase.CreateFolder("Assets", "Samples");
        }

        if (!AssetDatabase.IsValidFolder(SampleFolder))
        {
            AssetDatabase.CreateFolder("Assets/Samples", "ParentingShowcase");
        }

        if (!AssetDatabase.IsValidFolder(TimelineFolder))
        {
            AssetDatabase.CreateFolder(SampleFolder, "Timelines");
        }
    }

    private static void ResetAssets()
    {
        if (AssetDatabase.LoadAssetAtPath<Object>(TimelineFolder) != null)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:TimelineAsset", new[] { TimelineFolder }))
            {
                AssetDatabase.DeleteAsset(AssetDatabase.GUIDToAssetPath(guid));
            }
        }

        foreach (var p in new[] { ParentPath, SubPath })
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(p) != null)
            {
                AssetDatabase.DeleteAsset(p);
            }
        }
    }

    private static void Dirty(params Object[] objects)
    {
        foreach (var o in objects)
        {
            EditorUtility.SetDirty(o);
        }
    }
}
