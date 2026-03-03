# Chest Bind-Pose Position Debugging Notes

## The Problem

The knight chest (`knightchest` / `Padded_Cuirrass` SMR) renders with correct shape using runtime bind poses, but appears **below ground** — top of mesh at foot level, bottom several feet underground.

`updateWhenOffscreen = true` is currently set as a diagnostic workaround to defeat frustum culling (the mesh bounds are degenerate and Unity was culling it).

---

## Known Transform Values (from logs)

```
SMR: localPos=(0,0,0)  localRot=(270,0,0)  localScale=(100,100,100)
autoScale = 0.2346  (charredSpine=0.0101 / playerSpine=0.0431)
smrL2W.pos (SMR world origin) ≈ 1.26 world units below Hips bone world pos
Mesh bounds after clone: center=(0,0,0)  size=(0.08, 0.03, 0.05)  ← degenerate
```

Hips newBP matrix (what we're currently computing):
```
row0=(0.23, 0.00, 0.00, 0.00)
row1=(0.00, 0.00, 0.23, -0.02)
row2=(0.00,-0.23, 0.00, 0.00)
row3=(0.00, 0.00, 0.00, 1.00)
inverse.pos=(0.00, 0.00, 0.07)
```

Rows 1 and 2 are **swapped and negated** — the SMR's 270° X rotation is baked into every bind pose.

---

## Current Formula

```
BP[i] = bone[i].worldToLocalMatrix * smrL2W * scaleMat * Translate(-hipsBPMeshPos)
```

where `hipsBPMeshPos ≈ 0` (all shift candidates return ~zero), so effectively:

```
BP[i] = bone[i].worldToLocalMatrix * smrL2W * scaleMat
```

---

## Why All Shift Candidates Are ~Zero

Every attempted source for a vertical centering offset returns approximately zero:

| Source | Value |
|---|---|
| Player prefab BP inverse.pos for Hips | `(0, 0, -0.02)` |
| Charred BP inverse.pos for Hips | `(0, 0, 0.01)` |
| Charred BP inverse.pos for Spine2 | `(0, 0, 0.02)` |
| `InverseTransformPoint(hipsWorldPos)` | `(0, 0, 0.01)` |

**Conclusion:** Both player and Charred skeletons have Hips at the mesh origin in bind-pose space. There is no extractable vertical offset from bind poses or world-to-local transforms. A different approach is needed.

---

## Root Cause Hypothesis: The 270° X Rotation

The SMR has `localRot=(270,0,0)` baked in. This rotation **swaps Y↔Z axes and negates one**:

- A vertex at mesh-local `(0, +H, 0)` (pointing upward) ...
- After 270° X rotation becomes `(0, 0, -H)` (pointing into the ground)

This explains why the mesh appears inverted below the character. The same axis swap is visible in the Hips bind pose matrix (rows 1 and 2 swapped/negated).

This is also likely why the mesh "tracks like a ragdoll" — the bones are deforming the mesh along the wrong axes (Y↔Z swapped).

---

## Approaches Tried This Session

| # | Approach | Result |
|---|---|---|
| v0 "chest-formed-at-feet" | Runtime BP, shift from player prefab BP ≈ 0 | Top at knee, bottom at foot |
| v1 | World-space shift inserted *after* `smrL2W` | Mesh collapsed to a point (all bones same constant BP) |
| v2 | `InverseTransformPoint(hipsWorldPos)` as shift | Also ≈ zero, same as v0 |
| v3 (current) | `charredBPMap["Hips"].inverse` as shift | Also ≈ zero. `updateWhenOffscreen=true` added. Mesh below ground. |

---

## The Next Step to Try

**Factor out the SMR's 270° X rotation from the bind-pose formula.**

The SMR's `localToWorldMatrix` encodes both the 270° rotation and the 100x scale. When this is multiplied into the bind poses, the rotation inverts the mesh. We need to either:

### Option A: Strip rotation from smrL2W, apply separately

```csharp
// smrL2W encodes: Translation * Rotation(270x) * Scale(100)
// We want to apply Scale but compensate for the Rotation.
// Use only the scale+translation part, not the rotation:
var smrTRS = smr.transform;
var scaleOnlyMat = Matrix4x4.TRS(smrTRS.position, Quaternion.identity, smrTRS.lossyScale);
// Then: BP[i] = bone[i].W2L * scaleOnlyMat * scaleMat
```

But this loses the rotation entirely — may cause shape issues.

### Option B: Apply compensating counter-rotation

```csharp
// The SMR has -90° X (or equivalently 270° X). Counter-rotate by +90° X:
var counterRot = Matrix4x4.Rotate(Quaternion.Euler(-270f, 0f, 0f)); // = Euler(90,0,0)
var corrected = smrL2W * counterRot * scaleMat;
BP[i] = newBones[i].worldToLocalMatrix * corrected;
```

### Option C: Use smrL2W but account for the rotation in the shift

The correct "upward" direction in mesh space (post-rotation) is `smr.transform.up` in world space. Use this to construct an axis-aware shift after measuring the actual vertex Y-range.

### Option D: Measure actual mesh vertex bounds

`RecalculateBounds()` failed because the cloned mesh was still marked read-only. Fix:
```csharp
// Use Object.Instantiate (not new Mesh()) — or call mesh.MarkDynamic() first:
newMesh.MarkDynamic();  // must be called before modifying
newMesh.RecalculateBounds();
```

Then read `newMesh.bounds` to get the actual vertex Y-range. Use the center of that range as the shift.

---

## Other Issues to Address Later

- **Degenerate bounds**: The cloned mesh bounds are `(0.08, 0.03, 0.05)` in 100x local space ≈ tiny. `RecalculateBounds()` failed with "not allowed" — mesh was read-only. Need `mesh.MarkDynamic()` before modifying. `updateWhenOffscreen=true` is the current workaround.
- **Animation tracking**: Even once position is fixed, bone deformation axes are wrong (Y↔Z swapped due to 270° rotation). Both problems likely have the same root cause.
- **`autoScale=0.2346`**: This is `charredSpine / playerSpine = 0.0101 / 0.0431`. The charred spine measurement looks suspiciously small — worth verifying this isn't picking up the wrong bones or being affected by the same rotation issue.

---

## File Reference

All chest logic is in the `isChest` branch of `RemapArmorBones()` in:
`AshlandsReborn/Patches/CharredWarriorPatches.cs` — lines ~803–900
