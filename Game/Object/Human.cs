namespace CSkyL.Game.Object
{
    using CSkyL.Game.ID;
    using CSkyL.Transform;
    using CSkyL.UI;
    using System.Collections.Generic;
    using System.Linq;
    using Color = UnityEngine.Color;
    using Color32 = UnityEngine.Color32;

    public class Human : Object<HumanID>
    {
        public override string Name => manager.GetCitizenName(id._index);

        public bool IsTourist => _Is(Citizen.Flags.Tourist);
        public bool IsStudent => _Is(Citizen.Flags.Student);
        public VehicleID RiddenVehicleID => VehicleID._FromIndex(_citizen.m_vehicle);
        public BuildingID WorkBuildingID => BuildingID._FromIndex(_citizen.m_workBuilding);
        public BuildingID HomeBuildingID => BuildingID._FromIndex(_citizen.m_homeBuilding);

        protected bool _Is(Citizen.Flags flags) => (_citizen.m_flags & flags) != 0;

        internal static Human _Of(HumanID id)
            => Of(_GetPedestrianID(id)) is Pedestrian ped ? ped : new Human(id);

        protected Human(HumanID id) : base(id) => _citizen = _GetCitizen(id);

        protected readonly Citizen _citizen;

        private static Citizen _GetCitizen(HumanID hid)
            => manager.m_citizens.m_buffer[hid._index];
        private static PedestrianID _GetPedestrianID(HumanID hid)
            => PedestrianID._FromIndex(_GetCitizen(hid).m_instance);
        private static readonly CitizenManager manager = CitizenManager.instance;
    }

    public class Pedestrian : Human, IObjectToFollow
    {
        public override string Name => manager.GetCitizenName(id._index);

        public bool IsEnteringVehicle => _Is(CitizenInstance.Flags.EnteringVehicle);
        public bool IsHangingAround => _Is(CitizenInstance.Flags.HangAround);

        #region look ahead
        public uint GetTargetFrame()
        {
            uint i = (uint) (((int) id._index << 4) / 65536);
            return SimulationManager.instance.m_referenceFrameIndex - i;
        }

        private Position _GetFrame(uint simulationFrame)
        {
            uint index = simulationFrame >> 4 & 3U;
            return _frames[index];
        }

        public void SimulationFrame() {
            ref var human = ref GetCitizenInstance();
            _frames[human.m_lastFrame] = Position._FromVec(human.m_targetPos);
        }

        public void RenderOverlay(RenderManager.CameraInfo cameraInfo) {
            ref var human = ref GetCitizenInstance();
            uint targetFrame = GetTargetFrame();

            for (int i = 0; i < 4; i++) {
                // target position
                uint targetF = (uint) (targetFrame - (16 * i));
                var colorT = new Color32(255, (byte) (100 + 50 * i), (byte) (64 * i), 255);
                OverlayUtil.RenderCircle(cameraInfo, _GetFrame(targetF), colorT, 1.5f * (1 - .25f * i));
            }

            human.GetSmoothPosition(_pid, out var pos, out var rot);
            Positioning positioning = new Positioning(Position._FromVec(pos), Angle._FromQuat(rot));

            var lookDir0 = positioning.angle.ToDisplacement(20);
            var lookPos0 = positioning.position.Move(lookDir0);
            OverlayUtil.RenderArrow(cameraInfo, positioning.position, lookPos0, Color.blue);

            var lookPos1 = GetSmoothLookPos();
            var lookDir1 = positioning.position.DisplacementTo(lookPos1);
            OverlayUtil.RenderArrow(cameraInfo, positioning.position, lookPos1, Color.red);

            if (lookDir1.Distance > .1f) {
                Angle angle = Angle.Lerp(positioning.angle, lookDir1.AsLookingAngle(), .9f);
                var lookDir = angle.ToDisplacement(20);
                var lookPos = positioning.position.Move(lookDir);
                OverlayUtil.RenderArrow(cameraInfo, positioning.position, lookPos, Color.blue + Color.red);
            }
        }

        public Position GetSmoothLookPos()
        {
            ref var human = ref GetCitizenInstance();
            uint targetFrame = GetTargetFrame();
            Position pos1 = _GetFrame(targetFrame - 1 * 16U);
            Position pos2 = _GetFrame(targetFrame - 0 * 16U);
            float t = ((targetFrame & 15U) + SimulationManager.instance.m_referenceTimer) * 0.0625f;
            return Position.Lerp(pos1, pos2, t);
        }
        #endregion

        public Positioning GetPositioning()
        {
            ref var human = ref GetCitizenInstance();
            human.GetSmoothPosition(_pid, out var position, out var rotation);
            var positioning = new Positioning(Position._FromVec(position), Angle._FromQuat(rotation));

            var lookPos1 = GetSmoothLookPos();
            var lookDir1 = positioning.position.DisplacementTo(lookPos1);
            if (lookDir1.Distance > .1f) {
                positioning.angle = Angle.Lerp(positioning.angle, lookDir1.AsLookingAngle(), .9f);
            }
            return positioning;
        }
        public float GetSpeed() => GetCitizenInstance().GetLastFrameData().m_velocity.magnitude;
        public string GetStatus()
        {
            var _c = _citizen;
            var status = GetCitizenInstance().Info.m_citizenAI.GetLocalizedStatus(
                                pedestrianID._index, ref _c, out var implID);
            switch (ObjectID._FromIID(implID)) {
            case BuildingID bid: status += Building.GetName(bid); break;
            case NodeID nid:
                if (Node.GetTransitLineID(nid) is TransitID tid)
                    status += TransitLine.GetName(tid);
                break;
            }
            return status;
        }
        public Utils.Infos GetInfos()
        {
            Utils.Infos details = new Utils.Infos();

            string occupation;
            if (IsTourist) occupation = "(tourist)";
            else {
                occupation = Of(WorkBuildingID) is Building workBuilding ?
                                 (IsStudent ? "student at: " : "worker at: ") + workBuilding.Name :
                                 "(unemployed)";

                details["Home"] = Of(HomeBuildingID) is Building homeBuilding ?
                                      homeBuilding.Name : "(homeless)";
            }
            details["Occupation"] = occupation;

            return details;
        }
        public string GetPrefabName() => GetCitizenInstance().Info.name;

        public static IEnumerable<Pedestrian> GetIf(System.Func<Pedestrian, bool> filter)
        {
            return Enumerable.Range(1, manager.m_instanceCount)
                    .Select(i => Of(PedestrianID._FromIndex((ushort) i)) as Pedestrian)
                    .Where(p => p is Pedestrian && filter(p));
        }

        protected ref CitizenInstance GetCitizenInstance()
            => ref _GetCitizenInstance(pedestrianID);

        private static ref CitizenInstance _GetCitizenInstance(PedestrianID pid)
            => ref  manager.m_instances.m_buffer[pid._index];
        private static HumanID _GetHumanID(PedestrianID pid)
            => HumanID._FromIndex(_GetCitizenInstance(pid).m_citizen);

        internal static Pedestrian _Of(PedestrianID id)
            => _GetHumanID(id) is HumanID hid ? new Pedestrian(id, hid) : null;
        private Pedestrian(PedestrianID pid, HumanID hid) : base(hid)
        {
            pedestrianID = pid;
            _frames = new Position[4];
            ref var human = ref _GetCitizenInstance(pid);
            for (int i = 0; i < 4; ++i) {
                _frames[i] = Position._FromVec(human.m_targetPos);
            }
        }

        private bool _Is(CitizenInstance.Flags flags) => (GetCitizenInstance().m_flags & flags) != 0;

        public readonly PedestrianID pedestrianID;
        private ushort _pid => pedestrianID._index;
        private Position[] _frames;

        private static readonly CitizenManager manager = CitizenManager.instance;
    }
}
