using UnityEngine;
using UnityEngine.UI;

public class PlayerSlotView : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Text nameText;
    [SerializeField] private Text emptyText;
    [SerializeField] private Image readyIcon;
    [SerializeField] private Image hostIcon;

    public void SetEmpty()
    {
        if (nameText) nameText.gameObject.SetActive(false);
        if (readyIcon) readyIcon.gameObject.SetActive(false);
        if (hostIcon) hostIcon.gameObject.SetActive(false);

        if (emptyText)
        {
            emptyText.text = "´ë±â Áß...";
            emptyText.gameObject.SetActive(true);
        }
    }

    public void SetPlayer(string displayName, bool ready, bool isHost)
    {
        if (emptyText) emptyText.gameObject.SetActive(false);

        if (nameText)
        {
            nameText.text = string.IsNullOrEmpty(displayName) ? "Player" : displayName;
            nameText.gameObject.SetActive(true);
        }

        if (readyIcon) readyIcon.gameObject.SetActive(true);
        if (hostIcon) hostIcon.gameObject.SetActive(true);

        if (readyIcon) readyIcon.enabled = ready;
        if (hostIcon) hostIcon.enabled = isHost;
    }
}