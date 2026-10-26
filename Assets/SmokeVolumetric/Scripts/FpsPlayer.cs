using UnityEngine;
using UnityEngine.InputSystem;

namespace SmokeVolumetric
{
    /// <summary>
    /// Minimal first-person controller so the demo is actually playable.
    /// Uses the Input System package directly (no action asset required).
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public class FpsPlayer : MonoBehaviour
    {
        public Camera viewCamera;
        public float walkSpeed = 5.2f;
        public float sprintSpeed = 7.6f;
        public float jumpHeight = 1.05f;
        public float gravity = -19f;
        public float mouseSensitivity = 0.075f;
        public float throwCooldown = 0.55f;
        public float startPitch = 0f;

        static FpsPlayer s_Instance;

        /// <summary>
        /// Resolved lazily: static fields are wiped by a script domain reload,
        /// which would otherwise leave the HUD with a dead reference.
        /// </summary>
        public static FpsPlayer Instance
        {
            get
            {
                if (s_Instance == null)
                    s_Instance = FindFirstObjectByType<FpsPlayer>();
                return s_Instance;
            }
            private set => s_Instance = value;
        }

        public int GrenadesThrown { get; private set; }
        public float CooldownRemaining => Mathf.Max(0f, _nextThrow - Time.time);
        public float BobOffset { get; private set; }

        CharacterController _cc;
        float _pitch;
        float _yaw;
        float _verticalVelocity;
        float _nextThrow;
        float _bobPhase;
        Vector3 _spawnPoint;
        Quaternion _spawnRotation;
        Camera _cam;
        Vector3 _camLocalPos;

        void Awake()
        {
            Instance = this;
            _cc = GetComponent<CharacterController>();
            _spawnPoint = transform.position;
            _spawnRotation = transform.rotation;
            _yaw = transform.eulerAngles.y;
            _pitch = startPitch;

            if (viewCamera != null)
                _cam = viewCamera;
            else
                _cam = GetComponentInChildren<Camera>();

            if (_cam != null)
                _camLocalPos = _cam.transform.localPosition;

            LockCursor(true);
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        void Update()
        {
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            if (kb == null)
                return;

            if (kb.escapeKey.wasPressedThisFrame)
                LockCursor(false);
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
                LockCursor(true);

            bool locked = Cursor.lockState == CursorLockMode.Locked;

            // ---- look ----
            if (locked && mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue() * mouseSensitivity;
                _yaw += delta.x;
                _pitch = Mathf.Clamp(_pitch - delta.y, -85f, 85f);
                transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
                if (_cam != null)
                    _cam.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            }

            // ---- move ----
            Vector2 input = Vector2.zero;
            if (locked && kb != null)
            {
                if (kb.wKey.isPressed) input.y += 1f;
                if (kb.sKey.isPressed) input.y -= 1f;
                if (kb.dKey.isPressed) input.x += 1f;
                if (kb.aKey.isPressed) input.x -= 1f;
            }

            bool sprinting = locked && kb.leftShiftKey.isPressed;
            float speed = sprinting ? sprintSpeed : walkSpeed;
            Vector3 wish = (transform.right * input.x + transform.forward * input.y);
            if (wish.sqrMagnitude > 1f) wish.Normalize();

            Vector3 move = wish * speed;

            if (_cc.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -2f;

            if (locked && _cc.isGrounded && kb.spaceKey.wasPressedThisFrame)
                _verticalVelocity = Mathf.Sqrt(-2f * gravity * jumpHeight);

            _verticalVelocity += gravity * Time.deltaTime;
            move.y = _verticalVelocity;

            _cc.Move(move * Time.deltaTime);

            // ---- camera bob ----
            float planarSpeed = new Vector2(_cc.velocity.x, _cc.velocity.z).magnitude;
            _bobPhase += Time.deltaTime * (planarSpeed > 0.2f && _cc.isGrounded ? (sprinting ? 11f : 8f) : 0f);
            BobOffset = Mathf.Sin(_bobPhase) * 0.022f * Mathf.Clamp01(planarSpeed / walkSpeed);
            if (_cam != null)
                _cam.transform.localPosition = _camLocalPos + Vector3.up * BobOffset;

            // ---- actions ----
            if (locked && mouse != null && mouse.leftButton.wasPressedThisFrame && Time.time >= _nextThrow)
                ThrowGrenade(false);

            if (locked && mouse != null && mouse.rightButton.wasPressedThisFrame && Time.time >= _nextThrow)
                ThrowGrenade(true);

            if (locked && kb.cKey.wasPressedThisFrame)
                ClearSmoke();

            if (locked && kb.rKey.wasPressedThisFrame)
                Respawn();
        }

        void ThrowGrenade(bool dropAtFeet)
        {
            _nextThrow = Time.time + throwCooldown;
            GrenadesThrown++;

            Transform aim = _cam != null ? _cam.transform : transform;
            Vector3 origin = aim.position + aim.forward * 0.45f - Vector3.up * 0.08f;

            Vector3 velocity;
            if (dropAtFeet)
            {
                origin = transform.position + Vector3.up * 0.6f + transform.forward * 0.5f;
                velocity = transform.forward * 2.2f;
            }
            else
            {
                velocity = aim.forward * 13.5f + Vector3.up * 2.4f;
            }

            // carry a bit of the player's momentum
            Vector3 planar = _cc.velocity;
            planar.y = 0f;
            velocity += planar * 0.35f;

            SmokeGrenade.Throw(origin, velocity, dropAtFeet ? 0.9f : 1.55f);
        }

        void ClearSmoke()
        {
            foreach (var vol in FindObjectsByType<SmokeVolume>())
                Destroy(vol.gameObject);
        }

        void Respawn()
        {
            _cc.enabled = false;
            transform.position = _spawnPoint;
            transform.rotation = _spawnRotation;
            _verticalVelocity = 0f;
            _yaw = _spawnRotation.eulerAngles.y;
            _pitch = startPitch;
            _cc.enabled = true;
        }
    }
}
