package javax.rtcbench;

import javax.rtc.RTC;

public class RTCBenchmark {
    public static String name = "XXTEA";
    public static native void test_native();
    public static boolean test_java() {
        int NUMNUMBERS = 32;
        int numbers[] = new int[NUMNUMBERS]; // Not including this in the timing since we can't do it in C
        final int key[] = new int[] { 0, 1, 2, 3 };

        // Fill the array
        for (int i=0; i<NUMNUMBERS; i++)
            numbers[i] = (NUMNUMBERS - 1 - i);

        rtcbenchmark_measure_java_performance(numbers, key);

        final int desiredOutput[] = new int[] { -1953295707, 1851671290, -578878924, 226690120, -926180504, 130727213, 362104622, 1580496639, 995800627, -1025336115, 220929284, 503644809, -669280560, 1570416710, 1643492454, -1579579694, 956848904, -793882795, 1835568066, 1910888430, -259853652, 1118635228, -1694804444, 329572773, -145688030, 1996148599, 1759274902, 160864624, -2086794301, -38700629, -1692213011, -1750064035 };
        for (int i=0; i<NUMNUMBERS; i++) {
            if (numbers[i] != desiredOutput[i])
                return false;
        }

        return true;
    }
    
    // do btea
    public static void rtcbenchmark_measure_java_performance(int[] v, final int[] key) {
        RTC.startBenchmarkMeasurement_AOT();

        final int DELTA = 0x9e3779b9;
        short n = (byte)v.length; // Setting n to be 8 bit means we can't handle large arrays, but on a sensor node that should be fine)

        int y, z, sum;
        short p, e;
        byte rounds;
        if (n > 1) {          /* Coding Part */
            short n_minus_one = (short)(n-1);
            rounds = (byte)(6 + 52/n);
            sum = 0;
            z = v[n_minus_one];
            do {
                sum += DELTA;
                e = (byte)((sum >>> 2) & 3);
                for (p=0; p<n_minus_one; p++) {
                    y = v[p+(short)1]; 
                    z = v[p] += (((z>>>5^y<<2) + (y>>>3^z<<4)) ^ ((sum^y) + (key[(p&(short)3)^e] ^ z)));
                }
                y = v[(short)0];
                z = v[n_minus_one] += (((z>>>5^y<<2) + (y>>>3^z<<4)) ^ ((sum^y) + (key[(p&(short)3)^e] ^ z)));
            } while (--rounds != 0);
        } else if (n < -1) {  /* Decoding Part */
            n = (byte)-n;
            short n_minus_one = (short)(n-1);
            rounds = (byte)(6 + 52/n);
            sum = rounds*DELTA;
            y = v[(short)0];
            do {
                e = (byte)((sum >>> 2) & 3);
                for (p=(byte)n_minus_one; p>0; p--) {
                    z = v[p-(short)1];
                    y = v[p] -= (((z>>>5^y<<2) + (y>>>3^z<<4)) ^ ((sum^y) + (key[(p&(short)3)^e] ^ z)));
                }
                z = v[n_minus_one];
                y = v[(short)0] -= (((z>>>5^y<<2) + (y>>>3^z<<4)) ^ ((sum^y) + (key[(p&(short)3)^e] ^ z)));
                sum -= DELTA;
            } while (--rounds != 0);
        }
        RTC.stopBenchmarkMeasurement();
    }
}


