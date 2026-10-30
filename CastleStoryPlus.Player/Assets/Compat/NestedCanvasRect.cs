using UnityEngine;

namespace CastleStoryPlus.Compat
{
	// Unity 6 sizes a root screen-space canvas to the screen as soon as it is enabled; Unity 5.6 did that later, when it
	// drew. The game clones its UI templates as root objects and moves them under their canvas right after, so with
	// Unity 6 their nested canvases keep the screen's size (a small arrow sprite covering the screen). This puts back the
	// template's layout once the canvas is nested again.
	public sealed class NestedCanvasRect : MonoBehaviour
	{
		private Vector2 anchorMin;
		private Vector2 anchorMax;
		private Vector2 anchoredPosition;
		private Vector2 sizeDelta;
		private Vector2 pivot;
		private Vector3 localScale;

		public void Capture(RectTransform template)
		{
			anchorMin = template.anchorMin;
			anchorMax = template.anchorMax;
			anchoredPosition = template.anchoredPosition;
			sizeDelta = template.sizeDelta;
			pivot = template.pivot;
			localScale = template.localScale;
		}

		private void OnEnable()
		{
			Restore();
		}

		private void OnTransformParentChanged()
		{
			Restore();
		}

		private void OnCanvasHierarchyChanged()
		{
			Restore();
		}

		private void Restore()
		{
			Transform parent = transform.parent;
			if (parent == null || parent.GetComponentInParent<Canvas>() == null)
			{
				return;
			}
			var rect = (RectTransform)transform;
			rect.anchorMin = anchorMin;
			rect.anchorMax = anchorMax;
			rect.pivot = pivot;
			rect.sizeDelta = sizeDelta;
			rect.anchoredPosition = anchoredPosition;
			rect.localScale = localScale;
		}
	}
}
