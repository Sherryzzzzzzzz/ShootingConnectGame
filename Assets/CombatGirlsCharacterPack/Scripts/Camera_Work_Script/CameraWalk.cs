using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CombatGirlsCharacterPack
{
    public class CameraWalk : MonoBehaviour
    {
        [Header("카메라 이동속도")]
        public float moveSpeed = 10.0f;
        [Header("카메라 회전 감도(마우스)")]
        public float rotateSpeed = 500.0f;
        [Header("카메라 줌 속도")]
        public float zoomSpeed = 10.0f;
        [Header("카메라 줌 최소/최댓값")]
        public float minFov = 15.0f;
        public float maxFov = 90.0f;

        protected bool isCursorVisible = true;

        protected virtual void Start()
        {

        }

        protected virtual void Update()
        {
            myCameraWalk();
        }
        protected virtual void myCameraWalk()
        {
            // WASD로 카메라 이동
            float horizontal = Input.GetAxis("Horizontal") * moveSpeed * Time.deltaTime;
            float vertical = Input.GetAxis("Vertical") * moveSpeed * Time.deltaTime;
            transform.Translate(horizontal, 0, vertical);
            if (Input.GetKey(KeyCode.Q))
            {
                transform.Translate(0, -moveSpeed * Time.deltaTime, 0);
            }
            if (Input.GetKey(KeyCode.E))
            {
                transform.Translate(0, moveSpeed * Time.deltaTime, 0);
            }
            // 마우스 휠로 확대/축소
            float fov = GetComponent<Camera>().fieldOfView;
            fov -= Input.GetAxis("Mouse ScrollWheel") * zoomSpeed;
            fov = Mathf.Clamp(fov, minFov, maxFov);
            GetComponent<Camera>().fieldOfView = fov;

            // 오른쪽 마우스로 마우스 커서 표시/숨기기
            VisibleMouse();

            // 
            if (!isCursorVisible)
            {
                float mouseX = Input.GetAxis("Mouse X") * rotateSpeed * Time.deltaTime;
                float mouseY = Input.GetAxis("Mouse Y") * rotateSpeed * Time.deltaTime;

                transform.eulerAngles = new Vector3(transform.eulerAngles.x - mouseY, transform.eulerAngles.y + mouseX, 0);
            }
        }
        protected void VisibleMouse()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                isCursorVisible = !isCursorVisible;
                Cursor.visible = isCursorVisible;
                Cursor.lockState = isCursorVisible ? CursorLockMode.None : CursorLockMode.Locked;
            }
        }
    }
}