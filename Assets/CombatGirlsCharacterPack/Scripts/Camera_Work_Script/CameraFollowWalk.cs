using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace CombatGirlsCharacterPack
{
    public class CameraFollowWalk : CameraWalk
    {
        private Quaternion _initialRotation;
        private Transform _parent;
        private Vector3 initialLocalPosition;

        [Header("카메라가 캐릭터 따라 회전하기")]
        public bool isRotate = false;
        protected override void Start()
        {
            base.Start();

            _initialRotation = transform.rotation;
            _parent = transform.parent;
            initialLocalPosition = transform.position - _parent.position;
        }
        protected virtual void LateUpdate()
        {
            if (!isRotate)
            {
                transform.rotation = _initialRotation;
                transform.position = _parent.position + initialLocalPosition;
            }


        }
        protected override void myCameraWalk()
        {
            // WASD로 카메라 이동
            float horizontal = Input.GetAxis("Horizontal") * moveSpeed * Time.deltaTime;
            float vertical = Input.GetAxis("Vertical") * moveSpeed * Time.deltaTime;
            Vector3 prePos = transform.position;
            transform.Translate(horizontal, 0, vertical, Space.Self);
            initialLocalPosition += transform.position - prePos;
            //
            if (Input.GetKey(KeyCode.Q))
            {
                prePos = transform.position;
                transform.Translate(0, -moveSpeed * Time.deltaTime, 0, Space.Self);
                initialLocalPosition += transform.position - prePos;
            }
            if (Input.GetKey(KeyCode.E))
            {
                prePos = transform.position;
                transform.Translate(0, moveSpeed * Time.deltaTime, 0, Space.Self);
                initialLocalPosition += transform.position - prePos;
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
                _initialRotation = transform.rotation;
            }
        }
    }
}