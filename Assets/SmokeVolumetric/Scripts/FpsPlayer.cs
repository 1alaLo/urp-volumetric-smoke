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
                    s_Instance = FindAnyObjectByType<FpsPlayer>();
                return s_Instance;
            }
            private set => s_Instance = value;
        }

        public int GrenadesThrown { get; private set; }
        public int FragsThrown { get; private set; }
        public int ShotsFired => _weapon != null ? _weapon.ShotsFired : 0;
        public float CooldownRemaining => Mathf.Max(0f, _nextThrow - Time.time);
        public float BobOffset { get; private set; }

        CharacterController _cc;
        Weapon _weapon;
        float _pitch;
        float _yaw;
        float _verticalVelocity;
        float _nextThrow;
        float _bobPhase;
        Vector3 _spawnPoint;
        Quaternion _spawnRotation;
        Camera _cam;
        Vector3 _camLocalPos;

        float _shakeAmp;
        float _shakeTime;
        float _shakeDur;

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

            _weapon = GetComponent<Weapon>();
            if (_weapon == null)
                _weapon = gameObject.AddComponent<Weapon>();
            if (_weapon.viewCamera == null)
                _weapon.viewCamera = _cam;

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

            // ---- camera bob + shake ----
            float planarSpeed = new Vector2(_cc.velocity.x, _cc.velocity.z).magnitude;
            _bobPhase += Time.deltaTime * (planarSpeed > 0.2f && _cc.isGrounded ? (sprinting ? 11f : 8f) : 0f);
            BobOffset = Mathf.Sin(_bobPhase) * 0.022f * Mathf.Clamp01(planarSpeed / walkSpeed);

            Vector3 shakeOffset = Vector3.zero;
            if (_shakeTime > 0f)
            {
                _shakeTime -= Time.deltaTime;
                float k = Mathf.Clamp01(_shakeTime / Mathf.Max(0.01f, _shakeDur));
                shakeOffset = Random.insideUnitSphere * (_shakeAmp * k * k);
            }

            if (_cam != null)
                _cam.transform.localPosition = _camLocalPos + Vector3.up * BobOffset + shakeOffset;

            // ---- actions ----
            if (locked && mouse != null && mouse.leftButton.wasPressedThisFrame)
                _weapon.TryFire();

            if (locked && mouse != null && mouse.rightButton.wasPressedThisFrame)
                ThrowSmoke();

            if (locked && kb.fKey.wasPressedThisFrame)
                ThrowFrag();

            if (locked && kb.cKey.wasPressedThisFrame)
                ClearSmoke();

            if (locked && kb.rKey.wasPressedThisFrame)
                Respawn();
        }

        public void AddShake(float amplitude, float duration)
        {
            if (amplitude <= 0f)
                return;

            if (_shakeTime <= 0f)
                _shakeAmp = 0f;
            _shakeAmp = Mathf.Max(_shakeAmp, amplitude);
            _shakeDur = Mathf.Max(0.02f, duration);
            _shakeTime = _shakeDur;
        }

        /// <summary>
        /// Where a thrown canister should pop: at the aimed surface, at least
        /// <paramref name="minRange"/> away, and floating at least
        /// <paramref name="minHeight"/> above whatever is below it.
        /// </summary>
        Vector3 SolveBurstPoint(float minRange, float maxRange, float minHeight)
        {
            Transform aim = _cam != null ? _cam.transform : transform;
            Vector3 origin = aim.position;
            Vector3 dir = aim.forward;

            float dist = maxRange;
            if (Physics.Raycast(origin, dir, out RaycastHit hit, maxRange, ~0, QueryTriggerInteraction.Ignore)
                && !IsSelf(hit.collider))
            {
                dist = Mathf.Clamp(hit.distance, minRange, maxRange);
            }

            // If the crosshair is resting on smoke, burst where the ray enters
            // that cloud. Physics ignores the volume, so without this a grenade
            // sails through the puff and pops on the wall behind it - the blast
            // would then be hidden behind several metres of opaque smoke.
            var clouds = SmokeVolume.All;
            for (int i = 0; i < clouds.Count; i++)
            {
                var vol = clouds[i];
                if (vol == null || !vol.RaySphere(origin, dir, out float enter, out float exit))
                    continue;
                if (enter <= 0f || enter >= dist)
                    continue;

                dist = Mathf.Clamp(enter + 0.2f, minRange, maxRange);
                break;
            }

            Vector3 p = origin + dir * dist;
            p.y = Mathf.Max(p.y, GroundBelow(p) + minHeight);

            // never pop inside a wall - walk the point back along the aim ray
            for (int i = 0; i < 8; i++)
            {
                if (!Physics.CheckSphere(p, 0.45f, ~0, QueryTriggerInteraction.Ignore))
                    break;
                p -= dir * 0.6f;
                p.y = Mathf.Max(p.y, GroundBelow(p) + minHeight);
            }

            return p;
        }

        float GroundBelow(Vector3 p)
        {
            Vector3 from = p + Vector3.up * 0.5f;
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 30f, ~0, QueryTriggerInteraction.Ignore)
                && !IsSelf(hit.collider))
            {
                return hit.point.y;
            }
            return 0f;
        }

        bool IsSelf(Collider c)
        {
            return c.transform == transform || c.transform.IsChildOf(transform);
        }

        void ThrowSmoke()
        {
            if (Time.time < _nextThrow)
                return;

            _nextThrow = Time.time + throwCooldown;
            GrenadesThrown++;

            Transform aim = _cam != null ? _cam.transform : transform;
            Vector3 target = SolveBurstPoint(6f, 13f, 1.5f);
            Vector3 from = aim.position + aim.forward * 0.45f - Vector3.up * 0.06f;
            float flight = Mathf.Lerp(0.95f, 1.45f, Mathf.InverseLerp(6f, 13f, Vector3.Distance(from, target)));

            SmokeGrenade.Throw(from, target, flight);
        }

        void ThrowFrag()
        {
            if (Time.time < _nextThrow)
                return;

            _nextThrow = Time.time + throwCooldown;
            FragsThrown++;

            Transform aim = _cam != null ? _cam.transform : transform;
            Vector3 target = SolveBurstPoint(5f, 12f, 1.2f);
            Vector3 from = aim.position + aim.forward * 0.45f - Vector3.up * 0.06f;
            float flight = Mathf.Lerp(0.9f, 1.35f, Mathf.InverseLerp(5f, 12f, Vector3.Distance(from, target)));

            FragGrenade.Throw(from, target, flight);
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
