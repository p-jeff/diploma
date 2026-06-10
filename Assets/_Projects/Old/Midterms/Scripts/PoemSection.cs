using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Midterms
{
    public class PoemSection : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private TMP_Text poemText;
        [SerializeField] private GameObject infoText;

        [SerializeField] private Color hoverColor = Color.white;
        [SerializeField] private Color defaultColor = new Color(0.6f, 0.6f, 0.6f, 1f);

        private void Start()
        {
            if (poemText == null)
                poemText = transform.Find("PoemText")?.GetComponent<TMP_Text>();

            if (infoText == null)
            {
                var found = transform.Find("InfoText");
                if (found != null) infoText = found.gameObject;
            }

            if (poemText != null)
                poemText.color = defaultColor;

            if (infoText != null)
                infoText.SetActive(false);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (poemText != null)
                poemText.color = hoverColor;

            if (infoText != null)
                infoText.SetActive(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (poemText != null)
                poemText.color = defaultColor;

            if (infoText != null)
                infoText.SetActive(false);
        }
    }
}