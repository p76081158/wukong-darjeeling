package javax.rtc;

import javax.darjeeling.Darjeeling;

public class RTCTest implements IRTCTest {
	private static short static_short1;
	private static short static_short2;
	private static short static_short3;
	private static int static_int1;
	private static int static_int2;
	public static RTCTest static_ref1;
	public static RTCTest static_ref2;

	public short instance_short1;
	public int instance_int;
	public short instance_short2;

	public RTCTest() {}
	public RTCTest(int init_int_value) {
		this.instance_int = init_int_value;
	}

	// public static RTCTest check_new() {
	// 	RTCTest obj = new RTCTest(2914);
	// 	return obj;
	// }

	public static void check_calling_println() {
		System.out.println("Avenues all lined with trees.");
	}

	// public static RTCTest test_return_object(RTCTest obj) {
	// 	obj.instance_int++;
	// 	return obj;
	// }

	// public static int test_method_call_1(int a, short b, short c, int d) {
	// 	return test_method_call_1b(a, b, c, d);
	// }
	// public static int test_method_call_1b(int a, short b, short c, int d) {
	// 	// Just test a bunch of ints.
	// 	// This should be OK even without reserving space on the int stack since we pass more than will be returned, thus ensuring enough stack space for the return value.
	// 	return (a - b) + 1 + (d - c);
	// }
	// public static int test_method_call_2(int a, short b, RTCTest obj, short c, int d) {
	// 	return test_method_call_2b(a, b, obj, c, d);
	// }
	// public static int test_method_call_2b(int a, short b, RTCTest obj, short c, int d) {
	// 	// Add passing an object
	// 	return (a - b) + 1 + (d - c) + obj.instance_short1;
	// }
	// public static int test_method_call_3(RTCTest obj) {
	// 	return test_method_call_3b(obj);
	// }
	// public static int test_method_call_3b(RTCTest obj) {
	// 	// This method returns more than it gets passed on the int stack
	// 	// This will crash the VM if we don't reserve extra space on the system stack
	// 	return 100000+obj.instance_short1;
	// }
	// public static RTCTest test_method_call_4(RTCTest obj) {
	// 	return test_method_call_4b(obj);
	// }
	// public static RTCTest test_method_call_4b(RTCTest obj) {
	// 	// Test returning objects from rtc to rtc
	// 	obj.instance_int++;
	// 	return obj;
	// }
	// public static int test_method_call_5(RTCTest obj, int a) {
	// 	return obj.test_method_call_5b(a);
	// }
	// public int test_method_call_5b(int a) {
	// 	this.instance_int += a;
	// 	return this.instance_int;
	// }
	// public static int test_method_call_6(IRTCTest obj, RTCTest obj2) {
	// 	return obj.test_method_call_6b(obj2);
	// }
	// Can't comment this because we wouldn't be implemening IRTCTest anymore.
	public int test_method_call_6b(RTCTest obj) {
		this.instance_int += obj.instance_int;
		return this.instance_int;
	}
	// public static int test_method_call_7(int a) {
	// 	return Darjeeling.test_method_call_7b(a);
	// }

	// public static short test_method_call(short a, RTCTest obj) {
	// 	// return test_method_call2(a, (short)42, obj);
	// 	return (short)(test_method_call2(a, (short)42, obj) % (short)100);
	// }

	// public static short test_method_call2(short a, short b, RTCTest obj) {
	// 	// This should be OK even without reserving space on the int stack since we pass more than will be returned.
	// 	return (short)(a + b + obj.instance_short1);
	// }

	// public static int test_method_call2(short a, RTCTest obj) {
	// 	// Will return an int (4 bytes), but only consumer a short (2 bytes)
	// 	// This means we need to reserve 2 bytes on the real/int stack.
	// 	return a + 42 + obj.instance_short1;
	// }

	// public static void test_bubblesort(short[] numbers) {
	// 	short NUMNUMBERS = (short)numbers.length;
	// 	for (short i=0; i<(short)NUMNUMBERS; i++) {
	// 		for (short j=0; j<((short)(NUMNUMBERS-i-1)); j++) {
	// 			if (numbers[j]>numbers[j+1]) {
	// 				short temp = numbers[j];
	// 				numbers[j] = numbers[j+1];
	// 				numbers[((short)(j+1))] = temp;
	// 			}
	// 		}
	// 	}
	// // }

	// public static short test_short_ops(short x, short y, short op) {
	// 	if (op==0) return (short) ( -x);
	// 	if (op==1) return (short) (x + y);
	// 	if (op==2) return (short) (x - y);
	// 	if (op==3) return (short) (x * y);
	// 	if (op==4) return (short) (x / y);
	// 	if (op==5) return (short) (x % y);
	// 	if (op==6) return (short) (x & y);
	// 	if (op==7) return (short) (x | y);
	// 	if (op==8) return (short) (x ^ y);
	// 	if (op==9) return (short) (x + (-1));
	// 	if (op==10) return (short) (x + 42);
	// 	if (op==11) return (short) (x << y);
	// 	if (op==12) return (short) (x >> y);
	// 	if (op==13) return (short) (x >>> y);
	// 	return (short) (-42);
	// }

	// public static int test_int_ops(int x, int y, short op) {
	// 	if (op==0) return  -x;
	// 	if (op==1) return x + y;
	// 	if (op==2) return x - y;
	// 	if (op==3) return x * y;
	// 	if (op==4) return x / y;
	// 	if (op==5) return x % y;
	// 	if (op==6) return x & y;
	// 	if (op==7) return x | y;
	// 	if (op==8) return x ^ y;
	// 	if (op==9) return x + (-1);
	// 	if (op==10) return x + 42;
	// 	if (op==11) return x << y;
	// 	if (op==12) return x >> y;
	// 	if (op==13) return x >>> y;
	// 	return -42;
	// }

	// public static short test_fib(short x) {
	// 	if (x == 0)
	// 		return 0;
	// 	if (x == 1)
	// 		return 1;
	// 	short previous = 0;
	// 	short current = 1;
	// 	while (x != 1) {
	// 		short new_current = (short)(previous + current);
	// 		previous = current;
	// 		current = new_current;
	// 		x--;
	// 	}
	// 	return current;
	// }

	// public short test_instance_short1(short x) {
	// 	this.instance_short1 += x;
	// 	return this.instance_short1;
	// }

	// public short test_instance_short2(short x) {
	// 	this.instance_short2 += x;
	// 	return this.instance_short2;
	// }

	// public static short test_static_short(short x) {
	// 	static_short2 += x;
	// 	return static_short2;
	// }
	// public static short test_static_short_in_other_infusion_1(short x) {
	// 	Darjeeling.rtc_test_short1 += x;
	// 	return Darjeeling.rtc_test_short1;
	// }
	// public static short test_static_short_in_other_infusion_2(short x) {
	// 	Darjeeling.rtc_test_short2 += x;
	// 	return Darjeeling.rtc_test_short2;
	// }

	// public static int test_static_int(int x) {
	// 	static_int2 += x;
	// 	return static_int2;
	// }

	// public static int test_static_int_in_other_infusion_1(int x) {
	// 	Darjeeling.rtc_test_int1 += x;
	// 	return Darjeeling.rtc_test_int1;
	// }

	// public static int test_static_int_in_other_infusion_2(int x) {
	// 	Darjeeling.rtc_test_int2 += x;
	// 	return Darjeeling.rtc_test_int2;
	// }

	// public static void test_static_ref_swap() {
	// 	RTCTest obj = RTCTest.static_ref1;
	// 	RTCTest.static_ref1 = RTCTest.static_ref2;
	// 	RTCTest.static_ref2 = obj;
	// }

	// public static short compare_short_0_EQ(short x) {
	// 	if (x == 0)
	// 		return (short)1;
	// 	else
	// 		return (short)0;
	// }

	// public static short compare_short_0_NE(short x) {
	// 	if (x != 0)
	// 		return (short)1;
	// 	else
	// 		return (short)0;
	// }

	// public static short compare_short_0_LT(short x) {
	// 	if (x < 0)
	// 		return (short)1;
	// 	else
	// 		return (short)0;
	// }

	// public static short compare_short_0_LE(short x) {
	// 	if (x <= 0)
	// 		return (short)1;
	// 	else
	// 		return (short)0;
	// }

	// public static short compare_short_0_GT(short x) {
	// 	if (x > 0)
	// 		return (short)1;
	// 	else
	// 		return (short)0;
	// }

	// public static short compare_short_0_GE(short x) {
	// 	if (x >= 0)
	// 		return (short)1;
	// 	else
	// 		return (short)0;
	// }




	// public static short compare_short_EQ(short x, short y) {
	// 	if (x == y)
	// 		return (short)1;
	// 	else
	// 		return (short)0;
	// }

	// public static short compare_short_NE(short x, short y) {
	// 	if (x != y)
	// 		return (short)1;
	// 	else
	// 		return (short)0;
	// }

	// public static short compare_short_LT(short x, short y) {
	// 	if (x < y)
	// 		return (short)1;
	// 	else
	// 		return (short)0;
	// }

	// public static short compare_short_LE(short x, short y) {
	// 	if (x <= y)
	// 		return (short)1;
	// 	else
	// 		return (short)0;
	// }

	// public static short compare_short_GT(short x, short y) {
	// 	if (x > y)
	// 		return (short)1;
	// 	else
	// 		return (short)0;
	// }

	// public static short compare_short_GE(short x, short y) {
	// 	if (x >= y)
	// 		return (short)1;
	// 	else
	// 		return (short)0;
	// }

	// public static native short GetFortyThree();

	// public static short Add(short a, short b, short c, short d, short e) {
	// 	return (short)(a+b+c+d+e);
	// }
}
