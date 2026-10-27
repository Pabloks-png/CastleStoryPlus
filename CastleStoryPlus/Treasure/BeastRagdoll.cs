using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CastleStoryPlus.Treasure;

// A dead beast's model as a ragdoll: the model leaves the corpse for the world, each part gets a body and a box the
// size of its mesh, the legs, head, arms and tail hang on the body by hinges, and it is given a shove so it topples
// and falls to the ground. When the corpse goes (the game's "disintegrate", Crumble) the parts shrink away and break
// into small pieces of the beast that fall and vanish. Visual only and on every peer (Ignore Raycast layer, so no
// pointer or unit ray hits it); nothing of it is saved.
internal sealed class BeastRagdoll : MonoBehaviour
{
	private const int Layer = 2;

	private const float LieSeconds = 15f;

	private const float ShrinkSeconds = 0.5f;

	private const float PieceSeconds = 1.4f;

	private readonly List<Transform> _parts = new List<Transform>();

	private bool _crumbling;

	// Turns the model (its root, in the corpse) into a ragdoll in the world.
	internal static BeastRagdoll Create(Transform model, Dictionary<string, Transform> parts, GameObject corpse)
	{
		model.SetParent(null, true);
		BeastRagdoll ragdoll = model.gameObject.AddComponent<BeastRagdoll>();
		Collider[] corpseColliders = (corpse != null) ? corpse.GetComponentsInChildren<Collider>(true) : new Collider[0];
		parts.TryGetValue("Body", out Transform body);
		Rigidbody bodyRigid = null;
		if (body != null)
		{
			bodyRigid = ragdoll.Physical(body, 3f, corpseColliders);
		}
		foreach (KeyValuePair<string, Transform> part in parts)
		{
			if (part.Value == body)
			{
				continue;
			}
			Rigidbody rigid = ragdoll.Physical(part.Value, 0.6f, corpseColliders);
			if (bodyRigid != null)
			{
				HingeJoint hinge = part.Value.gameObject.AddComponent<HingeJoint>();
				hinge.connectedBody = bodyRigid;
				hinge.anchor = Vector3.zero;
				hinge.axis = Vector3.right;
				hinge.useLimits = true;
				hinge.limits = new JointLimits { min = -70f, max = 70f };
			}
			else
			{
				rigid.AddForce(Random.insideUnitSphere, ForceMode.VelocityChange);
			}
		}
		if (bodyRigid != null)
		{
			// A shove to one side and a spin about the length: it topples over.
			Vector3 side = model.right * ((Random.value < 0.5f) ? -1f : 1f);
			bodyRigid.AddForce(side * 1.6f + Vector3.up * 0.8f, ForceMode.VelocityChange);
			bodyRigid.AddTorque(model.forward * Random.Range(2f, 4f) * -Mathf.Sign(Vector3.Dot(side, model.right)), ForceMode.VelocityChange);
		}
		ragdoll.StartCoroutine(ragdoll.CrumbleLater(LieSeconds));
		return ragdoll;
	}

	private Rigidbody Physical(Transform part, float mass, Collider[] ignore)
	{
		part.gameObject.layer = Layer;
		MeshFilter filter = part.GetComponent<MeshFilter>();
		BoxCollider box = part.gameObject.AddComponent<BoxCollider>();
		if (filter != null && filter.sharedMesh != null)
		{
			Bounds bounds = filter.sharedMesh.bounds;
			box.center = bounds.center;
			box.size = Vector3.Max(bounds.size, Vector3.one * 0.08f);
		}
		foreach (Collider other in ignore)
		{
			if (other != null && other.enabled)
			{
				Physics.IgnoreCollision(box, other);
			}
		}
		Rigidbody rigid = part.gameObject.AddComponent<Rigidbody>();
		rigid.mass = mass;
		rigid.drag = 0.2f;
		rigid.angularDrag = 0.6f;
		rigid.interpolation = RigidbodyInterpolation.Interpolate;
		_parts.Add(part);
		return rigid;
	}

	// The beast breaks up: its parts shrink away, small pieces of it fall and vanish, then it is gone.
	internal void Crumble()
	{
		if (_crumbling)
		{
			return;
		}
		_crumbling = true;
		StopAllCoroutines();
		StartCoroutine(Crumbling());
	}

	private IEnumerator CrumbleLater(float seconds)
	{
		yield return new WaitForSeconds(seconds);
		Crumble();
	}

	private IEnumerator Crumbling()
	{
		List<Transform> pieces = new List<Transform>();
		foreach (Transform part in _parts)
		{
			if (part == null)
			{
				continue;
			}
			Renderer renderer = part.GetComponent<Renderer>();
			if (renderer == null)
			{
				continue;
			}
			Bounds bounds = renderer.bounds;
			float volume = bounds.size.x * bounds.size.y * bounds.size.z;
			int count = Mathf.Clamp(Mathf.RoundToInt(volume * 25f), 2, 9);
			for (int i = 0; i < count; i++)
			{
				pieces.Add(Piece(bounds, renderer.sharedMaterial));
			}
		}
		Dictionary<Transform, Vector3> sizes = new Dictionary<Transform, Vector3>();
		foreach (Transform part in _parts)
		{
			if (part != null)
			{
				sizes[part] = part.localScale;
			}
		}
		Dictionary<Transform, Vector3> pieceSizes = new Dictionary<Transform, Vector3>();
		foreach (Transform piece in pieces)
		{
			pieceSizes[piece] = piece.localScale;
		}
		float t = 0f;
		while (t < PieceSeconds)
		{
			t += Time.deltaTime;
			float shrink = 1f - Mathf.Clamp01(t / ShrinkSeconds);
			foreach (KeyValuePair<Transform, Vector3> part in sizes)
			{
				if (part.Key != null)
				{
					part.Key.localScale = part.Value * shrink;
				}
			}
			// The pieces keep their size a while, then shrink to nothing.
			float fade = 1f - Mathf.Clamp01((t - PieceSeconds * 0.5f) / (PieceSeconds * 0.5f));
			foreach (KeyValuePair<Transform, Vector3> piece in pieceSizes)
			{
				if (piece.Key != null)
				{
					piece.Key.localScale = piece.Value * fade;
				}
			}
			yield return null;
		}
		foreach (Transform piece in pieces)
		{
			if (piece != null)
			{
				Destroy(piece.gameObject);
			}
		}
		Destroy(gameObject);
	}

	// A small piece of the beast (its own material, so it shows the beast's colours) thrown out of the part.
	private static Transform Piece(Bounds bounds, Material material)
	{
		GameObject piece = GameObject.CreatePrimitive(PrimitiveType.Cube);
		piece.name = "BeastPiece";
		piece.layer = Layer;
		piece.transform.position = bounds.center + Vector3.Scale(Random.insideUnitSphere, bounds.extents * 0.8f);
		piece.transform.rotation = Random.rotation;
		piece.transform.localScale = Vector3.one * Random.Range(0.1f, 0.2f);
		Renderer renderer = piece.GetComponent<Renderer>();
		renderer.sharedMaterial = material;
		Rigidbody rigid = piece.AddComponent<Rigidbody>();
		rigid.mass = 0.05f;
		rigid.AddForce(Random.insideUnitSphere * 1.2f + Vector3.up * 1.5f, ForceMode.VelocityChange);
		rigid.AddTorque(Random.insideUnitSphere * 6f, ForceMode.VelocityChange);
		return piece.transform;
	}
}
