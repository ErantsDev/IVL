using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Manos del avatar del jugador:
///  - Separa las manos de la malla de piel en su propio material, para poder "ponerle" los guantes
///    de nitrilo (se ven en el espejo y en tercera persona).
///  - Crea una copia de los antebrazos y las manos que sí dibuja la cámara en primera persona
///    (el resto del avatar no se ve desde los ojos). Sólo se muestra mientras se acaricia al conejo,
///    para que el jugador vea su propia mano haciéndolo.
/// </summary>
public class AvatarHands : MonoBehaviour
{
    SkinnedMeshRenderer body;
    SkinnedMeshRenderer fpArms;
    AvatarAnimatorDriver driver;
    Material skinMat, gloveMat;
    int gloveSlot = -1;
    Mesh bodyMesh, armsMesh;

    public bool GlovesOn { get; private set; }
    public SkinnedMeshRenderer FirstPersonArms => fpArms;

    /// <summary>Prepara las manos del cuerpo del avatar. Se llama antes de crear las copias de sombra.</summary>
    public void Init(SkinnedMeshRenderer smr, Material glove)
    {
        body = smr;
        gloveMat = glove;
        driver = GetComponentInChildren<AvatarAnimatorDriver>();
        var src = smr.sharedMesh;
        var mats = new List<Material>(smr.sharedMaterials);
        int skinSub = mats.FindIndex(m => m != null && m.name.Contains("Skin"));
        int coatSub = mats.FindIndex(m => m != null && m.name.Contains("Coat"));
        if (skinSub < 0) return;

        var bones = smr.bones;
        var handBones = new HashSet<int>();
        var forearmBones = new HashSet<int>();
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == null) continue;
            if (bones[i].name == "LeftHand" || bones[i].name == "RightHand") handBones.Add(i);
            if (bones[i].name == "LeftLowerArm" || bones[i].name == "RightLowerArm") forearmBones.Add(i);
        }
        var bw = src.boneWeights;

        // 1) Manos fuera de la piel: nuevo submesh al final (material de piel o de guante).
        var skinTris = src.GetTriangles(skinSub);
        var keep = new List<int>();
        var hands = new List<int>();
        for (int t = 0; t + 2 < skinTris.Length; t += 3)
        {
            bool isHand = handBones.Contains(bw[skinTris[t]].boneIndex0) && handBones.Contains(bw[skinTris[t + 1]].boneIndex0) && handBones.Contains(bw[skinTris[t + 2]].boneIndex0);
            var list = isHand ? hands : keep;
            list.Add(skinTris[t]); list.Add(skinTris[t + 1]); list.Add(skinTris[t + 2]);
        }
        if (hands.Count == 0) return;

        bodyMesh = Instantiate(src);
        bodyMesh.name = src.name + "_Manos";
        int n = src.subMeshCount;
        bodyMesh.subMeshCount = n + 1;
        bodyMesh.SetTriangles(keep, skinSub);
        bodyMesh.SetTriangles(hands, n);
        smr.sharedMesh = bodyMesh;
        skinMat = mats[skinSub];
        mats.Add(skinMat);
        gloveSlot = mats.Count - 1;
        smr.sharedMaterials = mats.ToArray();

        // 2) Antebrazos (manga de la bata) + manos para la vista en primera persona.
        var sleeves = new List<int>();
        if (coatSub >= 0)
        {
            var coatTris = src.GetTriangles(coatSub);
            for (int t = 0; t + 2 < coatTris.Length; t += 3)
                if (forearmBones.Contains(bw[coatTris[t]].boneIndex0) && forearmBones.Contains(bw[coatTris[t + 1]].boneIndex0) && forearmBones.Contains(bw[coatTris[t + 2]].boneIndex0))
                { sleeves.Add(coatTris[t]); sleeves.Add(coatTris[t + 1]); sleeves.Add(coatTris[t + 2]); }
        }
        armsMesh = Instantiate(src);
        armsMesh.name = src.name + "_BrazosPrimeraPersona";
        armsMesh.subMeshCount = 2;
        armsMesh.SetTriangles(sleeves, 0);
        armsMesh.SetTriangles(hands, 1);

        var go = new GameObject("BrazosPrimeraPersona");
        go.layer = 0; // la cámara de los ojos sí dibuja esta capa
        go.transform.SetParent(smr.transform.parent, false);
        go.transform.localPosition = smr.transform.localPosition;
        go.transform.localRotation = smr.transform.localRotation;
        go.transform.localScale = smr.transform.localScale;
        fpArms = go.AddComponent<SkinnedMeshRenderer>();
        fpArms.sharedMesh = armsMesh;
        fpArms.bones = bones;
        fpArms.rootBone = smr.rootBone;
        fpArms.localBounds = smr.localBounds;
        fpArms.updateWhenOffscreen = true;
        fpArms.shadowCastingMode = ShadowCastingMode.Off;
        fpArms.sharedMaterials = new[] { coatSub >= 0 ? mats[coatSub] : skinMat, skinMat };
        fpArms.enabled = false;
        ApplyGloves();
    }

    public void SetGloves(bool on)
    {
        GlovesOn = on;
        ApplyGloves();
    }

    void ApplyGloves()
    {
        if (body == null || gloveSlot < 0) return;
        var hand = GlovesOn && gloveMat != null ? gloveMat : skinMat;
        var mats = body.sharedMaterials;
        mats[gloveSlot] = hand;
        body.sharedMaterials = mats;
        if (fpArms != null)
        {
            var m2 = fpArms.sharedMaterials;
            m2[1] = hand;
            fpArms.sharedMaterials = m2;
        }
    }

    void LateUpdate()
    {
        if (fpArms == null) return;
        bool show = driver != null && driver.PetWeight > 0.02f && !CameraView.ThirdPersonPreferred && GameFlow.GameplayInputAllowed;
        if (fpArms.enabled != show) fpArms.enabled = show;
    }

    void OnDestroy()
    {
        if (bodyMesh != null) Destroy(bodyMesh);
        if (armsMesh != null) Destroy(armsMesh);
    }
}
