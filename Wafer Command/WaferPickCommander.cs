#region File Header
//      Copyright (C) 2018 NIRA Systems.
//      All rights reserved. Reproduction or transmission in whole or in part, in
//      any form or by any means, electronic, mechanical or otherwise, is prohibited
//      without the prior written consent of the copyright owner.
//
// Release history:
//      Date            |    Author       |         Description
//----------------------------------------------------------------------------------------------
//      12-Oct-2018     |    Sumith C     |         Created and implemented. 
//----------------------------------------------------------------------------------------------
#endregion File Header

#region Namespaces

using System;
using System.Linq;
using System.Collections.Generic;
using Nira.SharedKernel;
using Nira.ToolControl.E90;
using Nira.ToolControlCommon;
using Nira.ToolControl.Modules;
using Nira.SharedKernel.Logging;
using Nira.ToolControlCommon.Utilities;
using Nira.ToolControl.ToolControlEventArgs;
using Nira.ToolControlCommon.ToolControlLogger;
using Nira.ToolControlCommon.Utilities.Alarm;

#endregion Namespaces

namespace Nira.ToolControl.Commands
{
    #region Class

    /// <summary>
    /// This is class used to validate and execute Wafter Pick command.
    /// This class is derived from ICommand Interface.
    /// </summary>
    public class WaferPickCommander : Command
    {
        #region Constants

        /// <summary>
        /// Represents the Slot#_PortSlot
        /// </summary>
        private const string PORT_SLOT = "Slot{0}_PortSlot";

        /// <summary>
        /// Represents the Hand#_Slot#_PortSlot
        /// </summary>
        private const string HAND_PORT_SLOT = "Hand{0}_Slot{1}_PortSlot";

        /// <summary>
        /// Represents the Slot#_ProcessStatus
        /// </summary>
        private const string PROCESS_STATUS = "Slot{0}_ProcessStatus";

        /// <summary>
        /// Represents the Hand#_Slot#_ProcessStatus
        /// </summary>
        private const string HAND_PROCESS_STATUS = "Hand{0}_Slot{1}_ProcessStatus";

        /// <summary>
        /// Represents the CurrentSlot
        /// </summary>
        private const string CURRENTSLOT = "CurrentSlot";

        #endregion Constants

        #region Member Variables

        /// <summary>
        /// Represents the transport module which creates this command.
        /// </summary>
        private Robot _robotModule;

        /// <summary>
        /// Contains the details of pick operation such as source and transfer hand details
        /// </summary>
        private PickCmdArgs _pickArgs;

        /// <summary>
        /// Method to hold the failure event args.
        /// </summary>
        private TransferEventArgs _failedOrCompletedEventArgs;

        #endregion Member Variables

        #region Constructor

        /// <summary>
        /// Constructor to subscribe pick events from loadport device.
        /// </summary>
        /// <param name="currentModule">Loadport module</param>
        /// <param name="pickArgs">The details of pick operation such as source and 
        /// transfer hand details</param>
        public WaferPickCommander(Robot currentModule, PickCmdArgs pickArgs):
            base(currentModule.Parent, CommandNames.PICK)
        {
            _robotModule = currentModule;
            _pickArgs = pickArgs;

            if (null != _robotModule &&
                null != _robotModule.RobotDevice)
            {
                _robotModule.RobotDevice.PickStarted += OnPickStarted;
                _robotModule.RobotDevice.PickCompleted += OnPickCompleted;
                _robotModule.RobotDevice.PickFailed += OnPickFailed;
            }
        }

        #endregion Constructor

		#region Properties
		
		/// <summary>
        /// To get/set the associated robot of this pick command.
        /// </summary>
        public Robot AssociatedRobot
		{
			get; set;
		}
		
		#endregion Properties
		
        #region Validate Command

        /// <summary>
        /// Method to validate whether the transport module, robot device,
        /// and source module is ready for pick.
        /// </summary>
        /// <returns>Returns success, if the TM and source modules are ready for pick
        /// Otherwise returns proper error code</returns>
        protected override ErrorCode ValidateCommand()
        {
            return Validate();
        }

        /// <summary>
        /// Method to validate the command.
        /// </summary>
        /// <returns>Success if the validation success, 
        /// otherwise returns proper errorCode</returns>
        private ErrorCode Validate()
        {
            ToolControlLogger.CommandsLogger.Log("WaferPickCommander:ValidateCommand", LogLevel.Information,
                  "Entering ValidateCommand");
            ErrorCode errorCode = ErrorCode.Success;
            do
            {
                if (null == _pickArgs)
                {
                    errorCode = ErrorCode.InvalidParameter;
                    break;
                }
                errorCode = ValidateTransportModule();
                if (ErrorCode.Success != errorCode)
                {
                    break;
                }
                IModule stationModule = ToolController.Instance.GetModule(_pickArgs.SourceModule);
                errorCode = ValidateStationModule(stationModule, _pickArgs.SourceSlot);

            } while (false);
            ToolControlLogger.CommandsLogger.Log("WaferPickCommander:ValidateCommand", LogLevel.Information,
                "Leaving ValidateCommand. Status:{0}", errorCode);
            return errorCode;
        }

        /// <summary>
        /// Method to validate whether TM is ready for pick.
        /// </summary>
        /// <returns>Success if the validation completed successfully, otherwise returns proper errorcode</returns>
        private ErrorCode ValidateTransportModule()
        {
            ErrorCode errorCode = ErrorCode.Success;
            do
            {
                if (!_robotModule.Parent.IsInitialized)
                {
                    errorCode = ErrorCode.DeviceNotInitialized;
                    break;
                }
                if (OperationMode.Offline == _robotModule.Parent.OperationMode)
                {
                    errorCode = ErrorCode.DeviceOffline;
                    break;
                }
                if (DeviceStatus.Idle != _robotModule.RobotDevice.DeviceStatus )
                {
                    errorCode = ErrorCode.DeviceBusy;
                    break;
                }
                errorCode = ValidateTransferHand();
                if (ErrorCode.Success != errorCode)
                {
                    break;
                }              
            } while (false);
            return errorCode;
        }

        /// <summary>
        /// Checks whether the robo hand  is ready for pick.
        /// </summary>
        /// <returns>Success if the hand is ready for transfer, otherwise returns proper errorcode</returns>
        private ErrorCode ValidateTransferHand()
        {
            RoboHand transferHand = _pickArgs.TransferHand;
            ErrorCode errorCode = ErrorCode.Success;
            do
            {
                if (RoboHand.None == transferHand)
                {
                    errorCode = ErrorCode.InvalidParameter;
                    break;
                }
                Hand currentHand = _robotModule.Parent.Hands.ToList().Find(hand =>
                                                      hand.Name == transferHand);
                if (null == currentHand)
                {
                    errorCode = ErrorCode.InvalidParameter;
                    break;
                }
                if (!currentHand.Enabled)
                {
                    errorCode = ErrorCode.RobotHandNotEnabled;
                    break;
                }
                if (currentHand.Slots.ToList().Exists(slot =>
                    SlotStatus.CorrectlyOccupied == slot.SlotStatus))
                {
                    errorCode = ErrorCode.TransferHandIsNotEmpty;
                }
            } while (false);
            return errorCode;
        }

        /// <summary>
        /// Checks whether the module is ready for pick.
        /// </summary>
        /// <param name="stationModule">The module to be validated</param>
        /// <param name="sourceSlot">The slot of the module to be validated</param>
        /// <returns>Success if the module is ready for transfer, otherwise returns proper errorcode</returns>
        private ErrorCode ValidateStationModule(IModule stationModule, int sourceSlot)
        {
            ErrorCode errCode = ErrorCode.Success;
            do
            {
                if (null == stationModule  ||
                    !_robotModule.Parent.AccesibleStations.Contains(stationModule.Name))
                {
                    break;
                }
                if (!stationModule.IsInitialized )
                {
                    errCode = ErrorCode.DeviceNotInitialized;
                    break;
                }
                if (OperationMode.Offline == stationModule.OperationMode )
                {
                    errCode = ErrorCode.DeviceOffline;
                    break;
                }
                if (stationModule is ILoadPortModule loadPortModule &&
                      string.IsNullOrEmpty(loadPortModule.LoadPortStation.Carrier))
                {
                    errCode = ErrorCode.CarrierNotPresent;
                    break;
                }
                if (0 == sourceSlot || stationModule.Slots.Count < sourceSlot)
                {
                    errCode = ErrorCode.UnsupportedOperation;
                    break;
                }
                if (SlotStatus.CorrectlyOccupied != stationModule.Slots[sourceSlot - 1].SlotStatus)
                {
                    errCode = ErrorCode.SourceIsEmpty;
                    break;
                }

            } while (false);
            return errCode;
        }
        
        #endregion Validate Command

        #region Execute Command

        /// <summary>
        /// Method to execute pick
        /// </summary>
        /// <returns>Returns success if the pick command send to device, otherwise
        protected override ErrorCode ExecuteCommand()
        {
            return DoExecuteCommand();
        }

        /// <summary>
        /// This function will be used to start execution
        /// </summary>
        /// <returns>Returns success, if the command send to device successfully.</returns>
        private ErrorCode DoExecuteCommand()
        {
            ToolControlLogger.CommandsLogger.Log("WaferPickCommander:ExecuteCommand", LogLevel.Information,
                "Pick command execution started");
            ErrorCode errCode = ErrorCode.FailedToExecute;
            //Get robot slot
            Hand transferHand = this._robotModule.Parent.Hands.ToList().Find(hand =>
                                                  hand.Name == _pickArgs.TransferHand);
            int capacity = transferHand.Slots.Count;

            int handStartSlot = 1;
            if(RoboHand.Hand2 == _pickArgs.TransferHand)
                handStartSlot = transferHand.Slots.Count + 1;

            //Get wafers involved in transfer
            List<IWafer> wafers = new List<IWafer>();
            IModule source = ToolController.Instance.GetModule(_pickArgs.SourceModule);
            int index = _pickArgs.SourceSlot - 1;
            if(index < source.Capacity)
            {
                IWafer wafer = source.Slots[index].Wafer;
                if (null != wafer)
                {
                    wafers.Add(wafer);
                }
            }

            //Track wafer to robo hand
            WaferTracker.GetInstance().TrackTransferTo(_robotModule.Parent, handStartSlot, wafers.AsReadOnly());

            UpdateWaferDetails(source, _pickArgs.TransferHand, 1, wafers); // Hardcoding to be removed

            if (_robotModule != null && _robotModule.RobotDevice != null)
            {
                errCode = _robotModule.RobotDevice.Pick(_pickArgs.TransferHand,
                    _pickArgs.SourceSlot, _pickArgs.SourceModule);
            }
            ToolControlLogger.CommandsLogger.Log("WaferPickCommander:ExecuteCommand", LogLevel.Information,
                "Executed Pick Command. ErrorCode is {0}", errCode);
            return errCode;
        }

        #endregion Execute Command

        #region UpdateWaferDetails

        /// <summary>
        /// Represents the method to update the wafer details which comming to robo hand
        /// </summary>
        /// <param name="hand">Hand</param>
        /// <param name="startingSlot">Starting slot</param>
        /// <param name="wafers">List of wafers</param>
        private void UpdateWaferDetails(IModule associatedModule, RoboHand hand, int startingSlot, List<IWafer> wafers)
        {
            IOHandler _ioHandler = IOHandler.GetInstance();
            int sourceStartingSlot = _pickArgs.SourceSlot;
            wafers.ForEach(delegate (IWafer waferObj)
            {
                Wafer wafer = waferObj as Wafer;
                string attributeName = string.Format(HAND_PORT_SLOT, (int)hand, startingSlot);
                string attributeValue = string.Format("{0}_{1}", wafer.SourceLoadPort, wafer.SourceSlot);
                ToolControlLogger.CommandsLogger.Log("WaferPickCommander:UpdateWaferDetails", LogLevel.Information,
                            "Updating {0} to {1}", attributeName, attributeValue);
                _ioHandler.WriteIOValue(_robotModule.RobotDevice.DeviceName, attributeName, attributeValue);

                attributeName = string.Format(HAND_PROCESS_STATUS, (int)hand, startingSlot);                
                ToolControlLogger.CommandsLogger.Log("WaferPickCommander:UpdateWaferDetails", LogLevel.Information,
                            "Updating {0} to {1}", attributeName, wafer.State);
                _ioHandler.WriteIOValue(_robotModule.RobotDevice.DeviceName, attributeName, wafer.State);

                startingSlot++;

                attributeName = string.Format(PORT_SLOT, sourceStartingSlot);
                attributeValue = string.Empty;
                ToolControlLogger.CommandsLogger.Log("WaferPickCommander:UpdateWaferDetails", LogLevel.Information,
                            "Updating {0} to {1}", attributeName, attributeValue);
                _ioHandler.WriteIOValue(associatedModule?.DeviceName, attributeName, attributeValue);

                attributeName = string.Format(PROCESS_STATUS, sourceStartingSlot);
                ToolControlLogger.CommandsLogger.Log("WaferPickCommander:UpdateWaferDetails", LogLevel.Information,
                            "Updating {0} to {1}", attributeName, SubstrateState.None);
                _ioHandler.WriteIOValue(associatedModule?.DeviceName, attributeName, SubstrateState.None);

                ToolControlLogger.CommandsLogger.Log("WaferPickCommander:UpdateWaferDetails", LogLevel.Information,
                             "Updating {0}.{1} to {2}", associatedModule?.Name, CURRENTSLOT, sourceStartingSlot);
                _ioHandler.WriteIOValue(associatedModule?.DeviceName, CURRENTSLOT, sourceStartingSlot);

                sourceStartingSlot++;
            });
        }

        #endregion UpdateWaferDetails

        #region OperationCompleted

        /// <summary>
        /// Method to fire operation completed.
        /// </summary>
        public override void FireModuleOperationCompleted()
        {
            _robotModule.OnPickCompleted(_failedOrCompletedEventArgs);
        }

        #endregion

        #region Event handlers

        /// <summary>
        /// This method gets invoked when the pick started event triggered from device
        /// </summary>
        /// <param name="sender">The device which sends this event</param>
        /// <param name="e">Event arguments</param>
        private void OnPickStarted(object sender, EventArgs e)
        {
            ToolControlLogger.CommandsLogger.Log("WaferPickCommander:OnPickStarted", LogLevel.Information,
                "Got PickStarted!!!");
            OnCommandStarted();
            _robotModule.OnPickStarted(e as TransferEventArgs);
        }

        /// <summary>
        /// This method gets invoked when the pick completed event triggered from device
        /// </summary>
        /// <param name="sender">The device which sends this event</param>
        /// <param name="e">Event arguments</param>
        private void OnPickCompleted(object sender, EventArgs e)
        {
            ToolControlLogger.CommandsLogger.Log("WaferPickCommander:OnPickCompleted", LogLevel.Information,
                "Got PickCompleted!!!");
            _failedOrCompletedEventArgs = e as TransferEventArgs;
            OnCommandCompleted();
        }

        /// <summary>
        /// This method gets invoked when the pick failed event triggered from device
        /// </summary>
        /// <param name="sender">The device which sends this event</param>
        /// <param name="e">Event arguments</param>
        private void OnPickFailed(object sender, EventArgs e)
        {
            ToolControlLogger.CommandsLogger.Log("WaferPickCommander:OnPickFailed", LogLevel.Information,
                 "Got PickFailed!!!");
            if (null != _executionTimedOutEvent)
            {
                _executionTimedOutEvent.Set();
            }
            _failedOrCompletedEventArgs = e as TransferEventArgs;

            string alarmName = string.Format("{0}_{1}", this._robotModule.Parent.Name.ToUpper(), Alarms.PICK_FAILED);

            Dictionary<string, RecoveryHandlerDelegate> recoveryNameActionTable =
                                                                new Dictionary<string, RecoveryHandlerDelegate>();

            recoveryNameActionTable[RecoveryActions.ABORT_PICK] = new RecoveryHandlerDelegate(OnAbort);
            recoveryNameActionTable[RecoveryActions.RETRY_PICK] = new RecoveryHandlerDelegate(OnRetry);
            AlarmHandler.GetInstance().RaiseError(new Error(alarmName,_failedOrCompletedEventArgs.Description, recoveryNameActionTable, null));
        }

        /// <summary>
        /// This method will be invoked by the base class when command validation/executioncommand fails
        /// </summary>
        protected override void OnCommandExecutionFailed(string description)
        {
            HandleCmdExecutionFailed(description);
        }
        /// <summary>
        /// This method will be invoked by the base class when command validation/executioncommand fails
        /// </summary>
        private void HandleCmdExecutionFailed(string description)
        {
            string alarmName = string.Format("{0}_{1}", this._robotModule.Parent.Name.ToUpper(), Alarms.PICK_FAILED);

            Dictionary<string, RecoveryHandlerDelegate> recoveryNameActionTable =
                                                                new Dictionary<string, RecoveryHandlerDelegate>();

            recoveryNameActionTable[RecoveryActions.ABORT_PICK] = new RecoveryHandlerDelegate(OnAbort);
            recoveryNameActionTable[RecoveryActions.RETRY_PICK] = new RecoveryHandlerDelegate(OnRetry);
            AlarmHandler.GetInstance().RaiseError(new Error(alarmName,description, recoveryNameActionTable, null));
        }
        #endregion Event handlers

        #region Alarm Recovery

        /// <summary>
        /// This method gets invoked when the user selects the Abort recovery option.
        /// </summary>
        /// <returns>True, if the failed event raised from module. Otherwise returns false</returns>
        protected override bool OnAbort(int alarmID, string recoveryName)
        {
            return HandleAbort(alarmID, recoveryName);
        }
        /// <summary>
        /// This method gets invoked when the user selects the Abort recovery option.
        /// </summary>
        /// <returns>True, if the failed event raised from module. Otherwise returns false</returns>
        private bool HandleAbort(int alarmID, string recoveryName)
        {
            if (CommandStatus.COMPLETED != CurrentStatus &&
               CommandStatus.FAILED != CurrentStatus &&
               CommandStatus.STOPPED != CurrentStatus)
            {
                OnCommandFailed();
                if (null == _failedOrCompletedEventArgs)
                {
                    _failedOrCompletedEventArgs = new TransferEventArgs(ToolControllerEvents.PICK_FAILED, _robotModule.Parent.Name)
                    {
                        CommandState = CommandState.Failed,
                        StationName = _pickArgs.SourceModule,
                        StationSlot = _pickArgs.SourceSlot,
                        TransferHand = _pickArgs.TransferHand
                    };
                }
                _robotModule.OnPickFailed(_failedOrCompletedEventArgs);
            }
            return true;
        }

        #endregion Alarm Recovery

        #region Command TimeOut

        /// <summary>
        /// This method gets invoked when the command gets timed out.
        /// </summary>
        protected override void OnCommandTimeOut()
        {
            string alarmName = string.Format("{0}_{1}", this._robotModule.Parent.Name.ToUpper(), Alarms.PICK_TIMEOUT);
            _timedOutAlarm = alarmName;
            Dictionary<string, RecoveryHandlerDelegate> recoveryNameActionTable =
                                                                new Dictionary<string, RecoveryHandlerDelegate>();

            recoveryNameActionTable[RecoveryActions.ABORT_PICK] = new RecoveryHandlerDelegate(OnAbort);
            AlarmHandler.GetInstance().RaiseError(new Error(alarmName, string.Empty, recoveryNameActionTable, null));
        }

        #endregion Command TimeOut

        #region UnSubscribe

        /// <summary>
        /// Method to cleanup resources of command
        /// </summary>
        protected override void CleanUp()
        {
            UnsubscribeEvents();
        }

        /// <summary>
        /// Method to unsubscribe events.
        /// </summary>
        private void UnsubscribeEvents()
        {
            if (null != _robotModule &&
                null != _robotModule.RobotDevice)
            {
                _robotModule.RobotDevice.PickStarted -= OnPickStarted;
                _robotModule.RobotDevice.PickCompleted -= OnPickCompleted;
                _robotModule.RobotDevice.PickFailed -= OnPickFailed;
            }
        }

        #endregion

    }
    #endregion Class
}
