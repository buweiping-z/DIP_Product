package com.dip.material.utils

import android.content.Context
import androidx.datastore.core.DataStore
import androidx.datastore.preferences.core.Preferences
import androidx.datastore.preferences.core.booleanPreferencesKey
import androidx.datastore.preferences.core.edit
import androidx.datastore.preferences.core.stringPreferencesKey
import androidx.datastore.preferences.preferencesDataStore
import kotlinx.coroutines.flow.Flow
import kotlinx.coroutines.flow.map

private val Context.dataStore: DataStore<Preferences> by preferencesDataStore(name = "dip_prefs")

class PreferencesManager(private val context: Context) {
    companion object {
        private val KEY_TOKEN = stringPreferencesKey("token")
        private val KEY_REFRESH_TOKEN = stringPreferencesKey("refresh_token")
        private val KEY_USERNAME = stringPreferencesKey("username")
        private val KEY_SERVER_URL = stringPreferencesKey("server_url")
        private val KEY_REMEMBER = booleanPreferencesKey("remember")
    }

    val token: Flow<String> = context.dataStore.data.map { it[KEY_TOKEN] ?: "" }
    val serverUrl: Flow<String> = context.dataStore.data.map { it[KEY_SERVER_URL] ?: "http://10.0.2.2:8800/" }

    suspend fun saveTokens(token: String, refreshToken: String) {
        context.dataStore.edit {
            it[KEY_TOKEN] = token
            it[KEY_REFRESH_TOKEN] = refreshToken
        }
    }

    suspend fun saveUsername(username: String) {
        context.dataStore.edit { it[KEY_USERNAME] = username }
    }

    suspend fun saveServerUrl(url: String) {
        context.dataStore.edit { it[KEY_SERVER_URL] = url }
    }

    suspend fun saveRemember(remember: Boolean) {
        context.dataStore.edit { it[KEY_REMEMBER] = remember }
    }

    suspend fun clear() {
        context.dataStore.edit { it.clear() }
    }
}
