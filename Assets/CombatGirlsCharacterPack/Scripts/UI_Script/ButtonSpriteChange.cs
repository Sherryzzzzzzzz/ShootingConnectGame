using UnityEngine;
using UnityEngine.UI;

namespace CombatGirlsCharacterPack
{
    public class ButtonSpriteChange : MonoBehaviour
    {
        private Button button;
        private Image buttonImage;
        private Text buttonText;
        private bool isPressed;
        public Sprite normalSprite;
        public Sprite hoverSprite;
        public Sprite pressedSprite;
        public GameObject normalText;
        public GameObject pressedText;

        private void Start()
        {
            button = GetComponent<Button>();
            buttonImage = button.image;
            buttonText = GetComponentInChildren<Text>();
            buttonImage.sprite = normalSprite;
            isPressed = false;
            button.onClick.AddListener(OnButtonClick);

            // 초기 상태에서는 normalText를 활성화하고 pressedText를 비활성화합니다.
            if (normalText != null)
                normalText.SetActive(true);
            if (pressedText != null)
                pressedText.SetActive(false);
        }

        private void Update()
        {
            if (isPressed)
            {
                buttonImage.sprite = pressedSprite;
                if (normalText != null)
                    normalText.SetActive(false);
                if (pressedText != null)
                    pressedText.SetActive(true);
            }
            else if (buttonImage.sprite != hoverSprite)
            {
                buttonImage.sprite = normalSprite;
                if (normalText != null)
                    normalText.SetActive(true);
                if (pressedText != null)
                    pressedText.SetActive(false);
            }
        }

        private void OnButtonClick()
        {
            isPressed = !isPressed;
        }
    }
}