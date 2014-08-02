using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Text;
using System.Timers;

using Sandbox.Common.ObjectBuilders;

using SEModAPIExtensions.API.Plugin;
using SEModAPIExtensions.API.Plugin.Events;

using SEModAPIInternal.API.Common;
using SEModAPIInternal.API.Entity;
using SEModAPIInternal.API.Entity.Sector.SectorObject;
using SEModAPIInternal.API.Entity.Sector.SectorObject.CubeGrid;
using SEModAPIInternal.API.Entity.Sector.SectorObject.CubeGrid.CubeBlock;
using SEModAPIInternal.Support;

using VRageMath;

namespace ReactorRadiationPlugin
{
	public class Core : PluginBase
	{
		#region "Attributes"

		private bool m_isActive;
		private Dictionary<CubeGridEntity, List<ReactorEntity>> m_reactorMap;

		private static float m_radiationRange;
		private static float m_damageRate;

		protected TimeSpan m_timeSinceLastUpdate;
		protected DateTime m_lastUpdate;
		protected DateTime m_lastFullScan;
		protected DateTime m_lastRadiationDamage;

		#endregion

		#region "Constructors and Initializers"

		public Core()
		{
			m_reactorMap = new Dictionary<CubeGridEntity, List<ReactorEntity>>();

			m_isActive = false;

			m_radiationRange = 5;
			m_damageRate = 1;

			m_lastUpdate = DateTime.Now;
			m_lastFullScan = DateTime.Now;
			m_lastRadiationDamage = DateTime.Now;
			m_timeSinceLastUpdate = DateTime.Now - m_lastUpdate;
		}

		#endregion

		#region "Properties"

		internal static float _RadiationRange
		{
			get { return m_radiationRange; }
			set { m_radiationRange = value; }
		}

		internal static float _DamageRate
		{
			get { return m_damageRate; }
			set { m_damageRate = value; }
		}

		[Category("Reactor Radiation Plugin")]
		[Browsable(true)]
		[ReadOnly(false)]
		public float RadiationRange
		{
			get { return m_radiationRange; }
			set { m_radiationRange = value; }
		}

		[Category("Reactor Radiation Plugin")]
		[Browsable(true)]
		[ReadOnly(false)]
		public float DamageRate
		{
			get { return m_damageRate; }
			set { m_damageRate = value; }
		}

		#endregion

		#region "Methods"

		#region "EventHandlers"

		public override void Init()
		{
			m_isActive = true;
		}

		public override void Update()
		{
			if (!m_isActive)
				return;

			m_timeSinceLastUpdate = DateTime.Now - m_lastUpdate;
			m_lastUpdate = DateTime.Now;

			TimeSpan timeSinceLastRadiation = DateTime.Now - m_lastRadiationDamage;
			if (timeSinceLastRadiation.TotalMilliseconds > 1000)
			{
				m_lastRadiationDamage = DateTime.Now;

				List<CharacterEntity> characters = SectorObjectManager.Instance.GetTypedInternalData<CharacterEntity>();

				foreach (var entry in m_reactorMap)
				{
					foreach (ReactorEntity reactor in entry.Value)
					{
						DoRadiationDamage(characters, reactor);
					}
				}
			}

			TimeSpan timeSinceLastFullScan = DateTime.Now - m_lastFullScan;
			if (timeSinceLastFullScan.TotalMilliseconds > 30000)
			{
				m_lastFullScan = DateTime.Now;

				CleanUpReactorMap();
				FullScan();
			}
		}

		public override void Shutdown()
		{
			m_isActive = false;
		}

		#endregion

		private void CleanUpReactorMap()
		{
			try
			{
				List<CubeGridEntity> cubeGridsToRemove = new List<CubeGridEntity>();
				foreach (var entry in m_reactorMap)
				{
					CubeGridEntity cubeGrid = entry.Key;
					if (cubeGrid.IsDisposed)
					{
						cubeGridsToRemove.Add(cubeGrid);
						continue;
					}

					List<ReactorEntity> reactorsToRemove = new List<ReactorEntity>(entry.Value);
					foreach (ReactorEntity reactor in entry.Value)
					{
						if (reactor != null && !reactor.IsDisposed)
							reactorsToRemove.Remove(reactor);
					}
					foreach (var item in reactorsToRemove)
					{
						entry.Value.Remove(item);
					}
				}
				foreach (var entry in cubeGridsToRemove)
				{
					m_reactorMap.Remove(entry);
				}
			}
			catch (Exception ex)
			{
				LogManager.GameLog.WriteLine(ex);
			}
		}

		private void FullScan()
		{
			try
			{
				List<CubeGridEntity> cubeGridList = SectorObjectManager.Instance.GetTypedInternalData<CubeGridEntity>();
				foreach (CubeGridEntity cubeGrid in cubeGridList)
				{
					if (cubeGrid == null || cubeGrid.IsDisposed)
						continue;
					if (cubeGrid.GridSizeEnum != MyCubeSize.Large)
						continue;
					if (cubeGrid.TotalPower <= 0)
						continue;

					if (!m_reactorMap.ContainsKey(cubeGrid))
					{
						m_reactorMap.Add(cubeGrid, new List<ReactorEntity>());
					}

					List<ReactorEntity> registeredReactors = m_reactorMap[cubeGrid];

					List<CubeBlockEntity> blocks = cubeGrid.CubeBlocks;
					foreach (CubeBlockEntity block in blocks)
					{
						if (block is ReactorEntity)
						{
							ReactorEntity reactor = (ReactorEntity)block;
							if (!registeredReactors.Contains(reactor))
							{
								registeredReactors.Add(reactor);
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				LogManager.GameLog.WriteLine(ex);
			}
		}

		private void DoRadiationDamage(List<CharacterEntity> targets, ReactorEntity source)
		{
			if (source == null)
				return;
			if (source.IsDisposed)
				return;
			if (!source.Enabled)
				return;

			Vector3I blockGridPos = source.Min;
			Matrix matrix = source.Parent.PositionAndOrientation.GetMatrix();
			Matrix orientation = matrix.GetOrientation();
			Vector3 rotatedBlockPos = Vector3.Transform((Vector3)blockGridPos * 2.5f, orientation);
			Vector3 reactorPos = rotatedBlockPos + source.Parent.Position;

			foreach (CharacterEntity character in targets)
			{
				double distance = Vector3.Distance(character.Position, reactorPos);

				float leakingRadiation = 0.1f + (0.5f * (1.0f / source.IntegrityPercent) - 0.5f);
				float reactorRange = 5.0f + 0.2f * source.Power;
				if (distance <= reactorRange)
				{
					double damage = _DamageRate * m_timeSinceLastUpdate.TotalSeconds * (reactorRange / distance) * leakingRadiation;
					character.Health = character.Health - (float)damage;
				}
			}
		}

		#endregion
	}
}
