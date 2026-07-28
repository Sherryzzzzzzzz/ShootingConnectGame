using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace CombatGirlsCharacterPack
{
    public class ButtonGenerator : MonoBehaviour
    {
        public List<RectTransform> buttonParents;
        public GameObject ButtonSample;
        public GameObject[] characters;
        public Vector3[] CustomerPositions;
        public Vector3[] CustomerRotations;
        public bool[] Pos_Use;
        public bool[] RT_Use;

        [System.Serializable]
        public class AnimationGroup
        {
            public string groupName;
            public int buttonParentNumber;
            public List<string> AnimationList;
            public List<GameObject> offObjects;
            public List<string> offObjectsAnimationList;

            public AnimationGroup(string name, int parentNumber)
            {
                groupName = name;
                buttonParentNumber = parentNumber;
                AnimationList = new List<string>();
                offObjectsAnimationList = new List<string>();
                offObjects = new List<GameObject>();
            }
        }

        public List<AnimationGroup> animationGroups;

        public float buttonSpacing = 50f;

        void Start()
        {
            ButtonSample.SetActive(true);
            foreach (AnimationGroup group in animationGroups)
            {
                CreateButtonsForGroup(group);
            }
            ButtonSample.SetActive(false);
        }

        void CreateButtonsForGroup(AnimationGroup group)
        {
            RectTransform buttonParent = buttonParents[group.buttonParentNumber];
            Vector3 buttonGroupPosition = buttonParent.localPosition;

            foreach (string animationName in group.AnimationList)
            {
                GameObject newButton = Instantiate(ButtonSample);
                newButton.transform.SetParent(buttonParent, false);
                Button cbutton = newButton.GetComponent<Button>();

                float buttonPositionY = buttonParent.childCount * buttonSpacing;
                newButton.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -buttonPositionY);

                if (cbutton.GetComponentInChildren<TextMeshProUGUI>())
                    cbutton.GetComponentInChildren<TextMeshProUGUI>().text = animationName;
                else
                    cbutton.GetComponentInChildren<Text>().text = animationName;

                string animationTriggerName = animationName;
                cbutton.onClick.AddListener(() =>
                {
                    for (int i = 0; i < characters.Length; i++)
                    {
                        GameObject character = characters[i];
                        if (character.activeSelf)
                        {
                            if (Pos_Use != null && Pos_Use.Length > i && Pos_Use[i])
                                character.transform.position = CustomerPositions[i];

                            if (RT_Use != null && RT_Use.Length > i && RT_Use[i])
                                character.transform.eulerAngles = CustomerRotations[i];

                            Animator charAnimator = character.GetComponent<Animator>();
                            charAnimator.SetTrigger(animationTriggerName);

                            if (group.offObjectsAnimationList.Contains(animationTriggerName))
                            {
                                foreach (GameObject offObject in group.offObjects)
                                {
                                    offObject.SetActive(false);
                                }
                            }
                            else
                            {
                                foreach (GameObject offObject in group.offObjects)
                                {
                                    offObject.SetActive(true);
                                }
                            }
                        }
                    }
                });
            }

            float buttonGroupHeight = buttonParent.childCount * buttonSpacing;
            buttonParent.sizeDelta = new Vector2(buttonParent.sizeDelta.x, buttonGroupHeight);
            buttonParent.localPosition = buttonGroupPosition;
        }
    }
}