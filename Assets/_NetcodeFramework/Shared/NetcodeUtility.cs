using System;
using System.Collections.Generic;
using UnityEngine.LowLevel;
using UnityEngine.PlayerLoop;

namespace NetcodeFramework {
    public static class NetcodeUtility {
        /// <summary>
        /// Inject the subsystems for netcode.
        /// </summary>
        /// <param name="systemType"></param>
        /// <param name="beforeUpdateDelegate"></param>
        /// <param name="afterUpdateDelegate"></param>
        public static void InjectSubsystems(Type systemType, Action beforeUpdateDelegate, Action afterUpdateDelegate) {
            PlayerLoopSystem rootSystem = PlayerLoop.GetCurrentPlayerLoop();
            List<PlayerLoopSystem> rootSubsystems = new List<PlayerLoopSystem>(rootSystem.subSystemList);
            for (int i = 0; i < rootSubsystems.Count; i++) {
                PlayerLoopSystem subsystem = rootSubsystems[i];
                if (subsystem.type == typeof(FixedUpdate)) {
                    List<PlayerLoopSystem> fixedUpdateSubsystems = new List<PlayerLoopSystem>(subsystem.subSystemList);
                    fixedUpdateSubsystems.Insert(0, new PlayerLoopSystem {
                        type = systemType,
                        updateDelegate = () => beforeUpdateDelegate()
                    });
                    fixedUpdateSubsystems.Add(new PlayerLoopSystem {
                        type = systemType,
                        updateDelegate = () => afterUpdateDelegate()
                    });
                    subsystem.subSystemList = fixedUpdateSubsystems.ToArray();
                    rootSubsystems[i] = subsystem;
                    break;
                }
            }
            rootSystem.subSystemList = rootSubsystems.ToArray();
            PlayerLoop.SetPlayerLoop(rootSystem);
        }

        /// <summary>
        /// Reset the subsystems.
        /// </summary>
        public static void ResetSubsystems() => PlayerLoop.SetPlayerLoop(PlayerLoop.GetDefaultPlayerLoop());
    }
}
