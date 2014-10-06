# vim: ts=4 sw=4
import os,sys,time
sys.path.append(os.path.join(os.path.dirname(os.path.abspath(__file__)), '..'))
from wkpf.models import *
from wkpf.locationTree import *
from wkpf.parser import *
from configuration import *
from wkpf.wkpfcomm import *
from wkpf.wuclasslibraryparser import *
from wkpf.wuapplication import *
import serial

class WuTest:
    def __init__(self):
        self.devs = TEST_DEVICES
        self.hexfiles = HEXFILES
        self.consoles = {}

        self.loadApplication(APP_PATH)
        assert len(self.devs) == len(self.hexfiles)
        self.dev_len = len(self.devs)

        # self.__downloadAll()

        self.comm = getComm()
        WuClassLibraryParser.read(COMPONENTXML_PATH)
        self.pair_devices_gateway()

    def pair_devices_gateway(self):
        self.constrollerReset()

        for dev in self.devs:
            self.stop()
            ret = self.wait('done')
            self.add()
            ret = self.wait('ready')
            print "----->", ret
            self.consoles[dev] = serial.Serial(dev, baudrate=115200)
            self.waitDeviceReady(dev)
            self.deviceReset(dev)
            self.deviceLearn(dev)
            self.wait('found',20)

    def loadApplication(self, dir_path):
        self.application = WuApplication(dir=dir_path)
        self.application.loadConfig()

    def __downloadAll(self):
        for i in xrange(len(self.devs)):
            self.__download(self.devs[i], self.hexfiles[i])

    def __download(self, port_name, hexfile):
        os.system("avrdude -p atmega2560 -c wiring -P %s -U flash:w:%s" % (port_name, hexfile))

    def add(self):
        self.comm.onAddMode()

    def stop(self):
        self.comm.onStopMode()

    def wait(self, findStr, timeout=5):
        print timeout
        while timeout > 0:
            st = self.comm.currentStatus()
            if st.find(findStr) != -1:
                return True
            time.sleep(1)
            timeout = timeout - 1
        return False

    def waitDeviceReady(self, dev, timeout=20):
        while timeout > 0:
            timeout = timeout - 1
            l = self.consoles[dev].readline()
            if l.find("ready") != -1: break

    def deviceLearn(self, dev):
        self.consoles[dev].write("$l")

    def deviceReset(self, dev):
        self.consoles[dev].write("$r")

    def constrollerReset(self):
        command = '../../tools/testrtt/a.out -d %s nowait controller reset' % (ZWAVE_GATEWAY_IP)
        os.system(command)

    def discovery(self, force=True):
        self.allNodesInfos = self.comm.getAllNodeInfos(force, 'nodes.xml')
        return self.allNodesInfos

    def setLocation(self, nodeID, location):
        self.comm.setLocation(nodeID, location)

    def getLocation(self, nodeID):
        return self.comm.getLocation(nodeID)

    def getProperty(self, nodeID, port, wuclassid, property_number):
        return self.comm.getProperty(nodeID, port, wuclassid, property_number)

    def setProperty(self, nodeID, port, wuclassid, property_number, datatype, value):
        self.comm.setProperty(nodeID, port, wuclassid, property_number, datatype, value)

    def getWuClassList(self, nodeID):
        return self.comm.getWuClassList(nodeID)

    def getWuObjectList(self, nodeID):
        return self.comm.getWuObjectList(nodeID)

    def getNodeInfo(self, nodeID):
        self.comm.getNodeInfo(nodeID)

    def countWuObjectByWuClassID(self, wuClassID):
        cnt = 0
        for node in self.allNodesInfos:
            for key in node.wuobjects:
                wuobject = node.wuobjects[key]
                if wuClassID == wuobject.wuclassdef.id:
                    cnt += 1

        return cnt

if __name__ == '__main__':
    test = WuTest()


