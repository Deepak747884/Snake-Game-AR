namespace GoogleARCore.Examples.CloudAnchors
{
    using GoogleARCore;
    using UnityEngine;
    using UnityEngine.Networking;

    // Set up touch input propagation while using Instant Preview in the editor.
    using Input = GoogleARCore.InstantPreviewInput;

    /// Controller for the Cloud Anchors Example. Handles the ARCore lifecycle.
    public class CloudAnchorsExampleController : MonoBehaviour
    {
        /// The UI Controller.
        public NetworkManagerUIController UIController;

        /// The root for ARCore-specific GameObjects in the scene.
        public GameObject ARCoreRoot;

        /// The helper that will calculate the World Origin offset when performing a raycast or
        /// generating planes.
        public ARCoreWorldOriginHelper ARCoreWorldOriginHelper;

        /// Indicates whether the Origin of the new World Coordinate System, i.e. the Cloud Anchor,
        /// was placed.
        private bool m_IsOriginPlaced = false;

        /// Indicates whether the Anchor was already instantiated.
        private bool m_AnchorAlreadyInstantiated = false;

        /// Indicates whether the Cloud Anchor finished hosting.
        private bool m_AnchorFinishedHosting = false;

        /// True if the app is in the process of quitting due to an ARCore connection error,
        /// otherwise false.
        private bool m_IsQuitting = false;

        /// The anchor component that defines the shared world origin.
        private Component m_WorldOriginAnchor = null;

        /// The last pose of the hit point from AR hit test.
        private Pose? m_LastHitPose = null;

        /// The current cloud anchor mode.
        private ApplicationMode m_CurrentMode = ApplicationMode.Ready;

        /// The Network Manager.
        private CloudAnchorsNetworkManager m_NetworkManager;
        
        /// Enumerates modes the example application can be in.
        public enum ApplicationMode
        {
            Ready,
            Hosting,
            Resolving,
        }

        public void Start()
        {
            m_NetworkManager = UIController.GetComponent<CloudAnchorsNetworkManager>();
            m_NetworkManager.OnClientConnected += _OnConnectedToServer;
            m_NetworkManager.OnClientDisconnected += _OnDisconnectedFromServer;

            // A Name is provided to the Game Object so it can be found by other Scripts
            // instantiated as prefabs in the scene.
            gameObject.name = "CloudAnchorsExampleController";
            ARCoreRoot.SetActive(false);
            
            _ResetStatus();
        }

        public void Update()
        {
            _UpdateApplicationLifecycle();

            // If we are neither in hosting nor resolving mode then the update is complete.
            if (m_CurrentMode != ApplicationMode.Hosting &&
                m_CurrentMode != ApplicationMode.Resolving)
            {
                return;
            }

            // If the origin anchor has not been placed yet, then update in resolving mode is
            // complete.
            if (m_CurrentMode == ApplicationMode.Resolving && !m_IsOriginPlaced)
            {
                return;
            }

            // If the player has not touched the screen then the update is complete.
            Touch touch;
            if (Input.touchCount < 1 || (touch = Input.GetTouch(0)).phase != TouchPhase.Began)
            {
                return;
            }

            TrackableHit arcoreHitResult = new TrackableHit();
            m_LastHitPose = null;

            // Raycast against the location the player touched to search for planes.
            if (Application.platform != RuntimePlatform.IPhonePlayer)
            {
                if (ARCoreWorldOriginHelper.Raycast(touch.position.x, touch.position.y,
                        TrackableHitFlags.PlaneWithinPolygon, out arcoreHitResult))
                {
                    m_LastHitPose = arcoreHitResult.Pose;
                }
            }
            
            // If there was an anchor placed, then instantiate the corresponding object.
            if (m_LastHitPose != null)
            {
                // The first touch on the Hosting mode will instantiate the origin anchor. Any
                // subsequent touch will instantiate a star, both in Hosting and Resolving modes.
                if (_CanPlaceStars())
                {
                    _InstantiateStar();
                }
                else if (!m_IsOriginPlaced && m_CurrentMode == ApplicationMode.Hosting)
                {
                    if (Application.platform != RuntimePlatform.IPhonePlayer)
                    {
                        m_WorldOriginAnchor =
                            arcoreHitResult.Trackable.CreateAnchor(arcoreHitResult.Pose);
                    }
                    

                    SetWorldOrigin(m_WorldOriginAnchor.transform);
                    _InstantiateAnchor();
                    OnAnchorInstantiated(true);
                }
            }
        }

        /// Sets the apparent world origin so that the Origin of Unity's World Coordinate System
        /// coincides with the Anchor. This function needs to be called once the Cloud Anchor is
        /// either hosted or resolved.
        public void SetWorldOrigin(Transform anchorTransform)
        {
            if (m_IsOriginPlaced)
            {
                Debug.LogWarning("The World Origin can be set only once.");
                return;
            }

            m_IsOriginPlaced = true;

            if (Application.platform != RuntimePlatform.IPhonePlayer)
            {
                ARCoreWorldOriginHelper.SetWorldOrigin(anchorTransform);
            }
            
        }

        /// Handles user intent to enter a mode where they can place an anchor to host or to exit
        /// this mode if already in it.
        public void OnEnterHostingModeClick()
        {
            if (m_CurrentMode == ApplicationMode.Hosting)
            {
                m_CurrentMode = ApplicationMode.Ready;
                _ResetStatus();
                Debug.Log("Reset ApplicationMode from Hosting to Ready.");
            }

            m_CurrentMode = ApplicationMode.Hosting;
            _SetPlatformActive();
        }

        /// Handles a user intent to enter a mode where they can input an anchor to be resolved or
        /// exit this mode if already in it.
        public void OnEnterResolvingModeClick()
        {
            if (m_CurrentMode == ApplicationMode.Resolving)
            {
                m_CurrentMode = ApplicationMode.Ready;
                _ResetStatus();
                Debug.Log("Reset ApplicationMode from Resolving to Ready.");
            }

            m_CurrentMode = ApplicationMode.Resolving;
            _SetPlatformActive();
        }

        /// Callback indicating that the Cloud Anchor was instantiated and the host request was
        /// made.
        public void OnAnchorInstantiated(bool isHost)
        {
            if (m_AnchorAlreadyInstantiated)
            {
                return;
            }

            m_AnchorAlreadyInstantiated = true;
            UIController.OnAnchorInstantiated(isHost);
        }

        /// Callback indicating that the Cloud Anchor was hosted.
        public void OnAnchorHosted(bool success, string response)
        {
            m_AnchorFinishedHosting = success;
            UIController.OnAnchorHosted(success, response);
        }

        /// Callback indicating that the Cloud Anchor was resolved.
        public void OnAnchorResolved(bool success, string response)
        {
            UIController.OnAnchorResolved(success, response);
        }

        /// Callback that happens when the client successfully connected to the server.
        private void _OnConnectedToServer()
        {
            if (m_CurrentMode == ApplicationMode.Hosting)
            {
                UIController.ShowDebugMessage("Find a plane, tap to create a Cloud Anchor.");
            }
            else if (m_CurrentMode == ApplicationMode.Resolving)
            {
                UIController.ShowDebugMessage("Waiting for Cloud Anchor to be hosted...");
            }
            else
            {
                _QuitWithReason("Connected to server with neither Hosting nor Resolving mode. " +
                                "Please start the app again.");
            }
        }

        /// Callback that happens when the client disconnected from the server.
        private void _OnDisconnectedFromServer()
        {
            _QuitWithReason("Network session disconnected! " +
                "Please start the app again and try another room.");
        }

        /// Instantiates the anchor object at the pose of the m_LastPlacedAnchor Anchor. This will
        /// host the Cloud Anchor.
        private void _InstantiateAnchor()
        {
            // The anchor will be spawned by the host, so no networking Command is needed.
            GameObject.Find("LocalPlayer").GetComponent<LocalPlayerController>()
                .SpawnAnchor(Vector3.zero, Quaternion.identity, m_WorldOriginAnchor);
        }

        /// Instantiates a star object that will be synchronized over the network to other clients.
        private void _InstantiateStar()
        {
            // Star must be spawned in the server so a networking Command is used.
            GameObject.Find("LocalPlayer").GetComponent<LocalPlayerController>()
                .CmdSpawnStar(m_LastHitPose.Value.position, m_LastHitPose.Value.rotation);
        }

        /// Sets the corresponding platform active.
        private void _SetPlatformActive()
        {
            if (Application.platform != RuntimePlatform.IPhonePlayer)
            {
                ARCoreRoot.SetActive(true);
                
            }
            else
            {
                ARCoreRoot.SetActive(false);
                
            }
        }

        /// Indicates whether a star can be placed.
        private bool _CanPlaceStars()
        {
            if (m_CurrentMode == ApplicationMode.Resolving)
            {
                return m_IsOriginPlaced;
            }

            if (m_CurrentMode == ApplicationMode.Hosting)
            {
                return m_IsOriginPlaced && m_AnchorFinishedHosting;
            }

            return false;
        }

        /// Resets the internal status.
        private void _ResetStatus()
        {
            // Reset internal status.
            m_CurrentMode = ApplicationMode.Ready;
            if (m_WorldOriginAnchor != null)
            {
                Destroy(m_WorldOriginAnchor.gameObject);
            }

            m_WorldOriginAnchor = null;
        }

        /// Check and update the application lifecycle.
        private void _UpdateApplicationLifecycle()
        {
            // Exit the app when the 'back' button is pressed.
            if (Input.GetKey(KeyCode.Escape))
            {
                Application.Quit();
            }

            var sleepTimeout = SleepTimeout.NeverSleep;

#if !UNITY_IOS
            // Only allow the screen to sleep when not tracking.
            if (Session.Status != SessionStatus.Tracking)
            {
                const int lostTrackingSleepTimeout = 15;
                sleepTimeout = lostTrackingSleepTimeout;
            }
#endif

            Screen.sleepTimeout = sleepTimeout;

            if (m_IsQuitting)
            {
                return;
            }

            // Quit if ARCore was unable to connect.
            if (Session.Status == SessionStatus.ErrorPermissionNotGranted)
            {
                _QuitWithReason("Camera permission is needed to run this application.");
            }
            else if (Session.Status.IsError())
            {
                _QuitWithReason("ARCore encountered a problem connecting. " +
                    "Please start the app again.");
            }
        }

        /// Quits the application after 5 seconds for the toast to appear.
        private void _QuitWithReason(string reason)
        {
            if (m_IsQuitting)
            {
                return;
            }

            UIController.ShowDebugMessage(reason);
            m_IsQuitting = true;
            Invoke("_DoQuit", 5.0f);
        }

        /// Actually quit the application.
        private void _DoQuit()
        {
            Application.Quit();
        }
    }
}
