# Add project specific ProGuard rules here.
# By default, the flags in this file are appended to flags specified
# in the Android SDK.

# Retrofit and OkHttp
-keepattributes Signature
-keepattributes RuntimeException
-keepattributes *Annotation*
-keepattributes SourceFile,LineNumberTable
-dontwarn java.awt.**
-dontwarn javax.naming.**
-dontwon sun.net.**
-dontwarn okhttp3.**
-dontwarn okio.**
-dontwarn org.**
-dontwarn jdk.internal.**

# Gson
-keepattributes Signature
-keepattributes *Annotation*
-dontwarn sun.misc.**
-keep class com.google.gson.typeadapters.** { *; }

# Room
-keepclassmembers class ** extends androidx.room.RoomDatabase {
    public static androidx.room.RoomDatabase$Companion Companion;
}
-keepclassmembers @androidx.room.Entity class * {
    @androidx.room.ForeignKey *;
}
