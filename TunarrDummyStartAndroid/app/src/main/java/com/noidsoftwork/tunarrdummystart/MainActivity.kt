package com.noidsoftwork.tunarrdummystart

import android.content.Context
import android.content.SharedPreferences
import android.content.res.ColorStateList
import android.os.Bundle
import android.view.View
import android.view.inputmethod.InputMethodManager
import android.widget.Toast
import androidx.appcompat.app.AppCompatActivity
import androidx.core.content.ContextCompat
import androidx.recyclerview.widget.LinearLayoutManager
import com.google.android.material.tabs.TabLayout
import com.google.gson.Gson
import com.noidsoftwork.tunarrdummystart.databinding.ActivityMainBinding
import kotlinx.coroutines.*
import okhttp3.*
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.RequestBody.Companion.toRequestBody
import java.io.IOException
import java.lang.ref.WeakReference

class MainActivity : AppCompatActivity() {

    private lateinit var binding: ActivityMainBinding
    private val client = OkHttpClient()
    private val gson = Gson()
    private var serverUrl = ""
    private var isConnected = false
    private var refreshJob: Job? = null
    private var channelsAdapter = ChannelsAdapter()
    private var configPopulated = false
    private var currentConfig: AppConfig? = null

    private lateinit var prefs: SharedPreferences

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        binding = ActivityMainBinding.inflate(layoutInflater)
        setContentView(binding.root)

        prefs = getSharedPreferences("TunarrDummyPrefs", Context.MODE_PRIVATE)

        // Initialize RecyclerView
        binding.rvChannels.layoutManager = LinearLayoutManager(this)
        binding.rvChannels.adapter = channelsAdapter

        // Load saved server address
        val savedUrl = prefs.getString("server_url", "http://10.0.2.2:1290")
        binding.etServerAddress.setText(savedUrl)

        // Setup Connect Button
        binding.btnConnect.setOnClickListener {
            if (isConnected) {
                disconnectFromServer()
            } else {
                connectToServer()
            }
        }

        // Setup Start/Stop Button
        binding.btnStartStop.setOnClickListener {
            toggleRunner()
        }

        // Setup Refresh Listener
        binding.swipeRefreshLayout.setOnRefreshListener {
            refreshData(silent = false)
        }

        // Setup Config Save Button
        binding.btnSaveConfig.setOnClickListener {
            saveConfiguration()
        }

        // Setup TabLayout switching
        binding.tabLayout.addOnTabSelectedListener(object : TabLayout.OnTabSelectedListener {
            override fun onTabSelected(tab: TabLayout.Tab?) {
                when (tab?.position) {
                    0 -> {
                        binding.swipeRefreshLayout.visibility = View.VISIBLE
                        binding.scrollLogs.visibility = View.GONE
                        binding.scrollConfig.visibility = View.GONE
                    }
                    1 -> {
                        binding.swipeRefreshLayout.visibility = View.GONE
                        binding.scrollLogs.visibility = View.VISIBLE
                        binding.scrollConfig.visibility = View.GONE
                    }
                    2 -> {
                        binding.swipeRefreshLayout.visibility = View.GONE
                        binding.scrollLogs.visibility = View.GONE
                        binding.scrollConfig.visibility = View.VISIBLE
                        if (!configPopulated) {
                            fetchConfig()
                        }
                    }
                }
            }
            override fun onTabUnselected(tab: TabLayout.Tab?) {}
            override fun onTabReselected(tab: TabLayout.Tab?) {}
        })
    }

    override fun onResume() {
        super.onResume()
        if (isConnected && serverUrl.isNotEmpty()) {
            startPeriodicRefresh()
        }
    }

    override fun onStop() {
        super.onStop()
        stopPeriodicRefresh()
    }

    private fun connectToServer() {
        var inputUrl = binding.etServerAddress.text.toString().trim()
        if (inputUrl.isEmpty()) {
            Toast.makeText(this, "Please enter a server address", Toast.LENGTH_SHORT).show()
            return
        }

        if (!inputUrl.startsWith("http://") && !inputUrl.startsWith("https://")) {
            inputUrl = "http://$inputUrl"
        }

        if (inputUrl.endsWith("/")) {
            inputUrl = inputUrl.substring(0, inputUrl.length - 1)
        }

        binding.btnConnect.isEnabled = false
        binding.etServerAddress.isEnabled = false

        CoroutineScope(Dispatchers.Main).launch {
            val success = testConnection(inputUrl)
            binding.btnConnect.isEnabled = true
            
            if (success) {
                serverUrl = inputUrl
                isConnected = true
                
                // Save URL in Prefs
                prefs.edit().putString("server_url", serverUrl).apply()

                // Hide Keyboard
                val imm = getSystemService(Context.INPUT_METHOD_SERVICE) as? InputMethodManager
                imm?.hideSoftInputFromWindow(binding.etServerAddress.windowToken, 0)

                // Update UI state
                binding.btnConnect.text = "Disconnect"
                binding.btnConnect.backgroundTintList = ColorStateList.valueOf(
                    ContextCompat.getColor(this@MainActivity, R.color.state_failed)
                )
                binding.controlPanel.visibility = View.VISIBLE
                binding.tabLayout.visibility = View.VISIBLE
                binding.tabContentContainer.visibility = View.VISIBLE

                configPopulated = false
                fetchConfig()
                startPeriodicRefresh()
            } else {
                binding.etServerAddress.isEnabled = true
                Toast.makeText(this@MainActivity, "Failed to connect to server", Toast.LENGTH_LONG).show()
            }
        }
    }

    private fun disconnectFromServer() {
        stopPeriodicRefresh()
        isConnected = false
        serverUrl = ""
        configPopulated = false
        
        binding.btnConnect.text = "Connect"
        binding.btnConnect.backgroundTintList = ColorStateList.valueOf(
            ContextCompat.getColor(this, R.color.accent_color)
        )
        binding.etServerAddress.isEnabled = true
        binding.controlPanel.visibility = View.GONE
        binding.tabLayout.visibility = View.GONE
        binding.tabContentContainer.visibility = View.GONE
    }

    private suspend fun testConnection(url: String): Boolean = withContext(Dispatchers.IO) {
        val request = Request.Builder()
            .url("$url/api/status")
            .build()
        try {
            client.newCall(request).execute().use { response ->
                response.isSuccessful
            }
        } catch (e: Exception) {
            false
        }
    }

    private fun startPeriodicRefresh() {
        refreshJob?.cancel()
        refreshJob = CoroutineScope(Dispatchers.Main).launch {
            while (isActive) {
                refreshData(silent = true)
                delay(3000)
            }
        }
    }

    private fun stopPeriodicRefresh() {
        refreshJob?.cancel()
        refreshJob = null
    }

    private fun refreshData(silent: Boolean) {
        if (serverUrl.isEmpty()) return
        
        if (!silent) {
            binding.swipeRefreshLayout.isRefreshing = true
        }

        CoroutineScope(Dispatchers.Main).launch {
            val statusDef = async(Dispatchers.IO) { fetchStatusFromApi() }
            val logsDef = async(Dispatchers.IO) { fetchLogsFromApi() }

            val status = statusDef.await()
            val logs = logsDef.await()

            if (!silent) {
                binding.swipeRefreshLayout.isRefreshing = false
            }

            if (status != null) {
                updateRunnerStatusUi(status.isRunning)
                channelsAdapter.updateItems(status.channels)
            } else {
                // Connection lost?
                if (isConnected) {
                    Toast.makeText(this@MainActivity, "Connection lost to server", Toast.LENGTH_SHORT).show()
                    disconnectFromServer()
                }
            }

            if (logs != null) {
                binding.tvLogs.text = logs
                // Auto scroll logs to bottom
                binding.scrollLogs.post {
                    binding.scrollLogs.fullScroll(View.FOCUS_DOWN)
                }
            }
        }
    }

    private fun fetchStatusFromApi(): ServerStatusResponse? {
        val request = Request.Builder()
            .url("$serverUrl/api/status")
            .build()
        return try {
            client.newCall(request).execute().use { response ->
                if (response.isSuccessful && response.body != null) {
                    gson.fromJson(response.body!!.string(), ServerStatusResponse::class.java)
                } else null
            }
        } catch (e: Exception) {
            null
        }
    }

    private fun fetchLogsFromApi(): String? {
        val request = Request.Builder()
            .url("$serverUrl/api/log")
            .build()
        return try {
            client.newCall(request).execute().use { response ->
                if (response.isSuccessful && response.body != null) {
                    response.body!!.string()
                } else null
            }
        } catch (e: Exception) {
            null
        }
    }

    private fun fetchConfig() {
        if (serverUrl.isEmpty()) return
        CoroutineScope(Dispatchers.Main).launch {
            val config = withContext(Dispatchers.IO) {
                val request = Request.Builder()
                    .url("$serverUrl/api/config")
                    .build()
                try {
                    client.newCall(request).execute().use { response ->
                        if (response.isSuccessful && response.body != null) {
                            gson.fromJson(response.body!!.string(), AppConfig::class.java)
                        } else null
                    }
                } catch (e: Exception) {
                    null
                }
            }

            if (config != null) {
                currentConfig = config
                populateConfigUi(config)
                configPopulated = true
            }
        }
    }

    private fun populateConfigUi(config: AppConfig) {
        binding.etBaseUrl.setText(config.baseUrl)
        binding.etChannelCount.setText(config.channelCount.toString())
        binding.etStartupDelay.setText(config.startupDelaySeconds.toString())
        binding.etStaggerDelay.setText(config.staggerDelayMs.toString())
        binding.etRetryCount.setText(config.retryCount.toString())
        binding.etFfmpegPath.setText(config.ffmpegPath)
        binding.etHwAccel.setText(config.hwAccel)
        binding.etThreads.setText(config.threadsPerProcess.toString())
        binding.etWebPort.setText(config.webserverPort.toString())

        binding.swAutoStart.isChecked = config.autoStartOnLaunch
        binding.swStartWindows.isChecked = config.startWithWindows
        binding.swWebserverEnabled.isChecked = config.enableWebserver
    }

    private fun saveConfiguration() {
        if (serverUrl.isEmpty() || currentConfig == null) return

        val config = AppConfig(
            baseUrl = binding.etBaseUrl.text.toString().trim(),
            channelCount = binding.etChannelCount.text.toString().toIntOrNull() ?: 1,
            startupDelaySeconds = binding.etStartupDelay.text.toString().toIntOrNull() ?: 0,
            staggerDelayMs = binding.etStaggerDelay.text.toString().toIntOrNull() ?: 0,
            retryCount = binding.etRetryCount.text.toString().toIntOrNull() ?: 0,
            ffmpegPath = binding.etFfmpegPath.text.toString().trim(),
            hwAccel = binding.etHwAccel.text.toString().trim(),
            threadsPerProcess = binding.etThreads.text.toString().toIntOrNull() ?: 0,
            webserverPort = binding.etWebPort.text.toString().toIntOrNull() ?: 1290,
            autoStartOnLaunch = binding.swAutoStart.isChecked,
            startWithWindows = binding.swStartWindows.isChecked,
            enableWebserver = binding.swWebserverEnabled.isChecked,
            channels = currentConfig!!.channels // Preserve channels list
        )

        binding.btnSaveConfig.isEnabled = false

        CoroutineScope(Dispatchers.Main).launch {
            val success = withContext(Dispatchers.IO) {
                val mediaType = "application/json; charset=utf-8".toMediaType()
                val body = gson.toJson(config).toRequestBody(mediaType)
                val request = Request.Builder()
                    .url("$serverUrl/api/config")
                    .post(body)
                    .build()
                try {
                    client.newCall(request).execute().use { response ->
                        if (response.isSuccessful && response.body != null) {
                            val apiResp = gson.fromJson(response.body!!.string(), ApiResponse::class.java)
                            apiResp.success
                        } else false
                    }
                } catch (e: Exception) {
                    false
                }
            }

            binding.btnSaveConfig.isEnabled = true
            if (success) {
                Toast.makeText(this@MainActivity, "Configuration saved successfully", Toast.LENGTH_SHORT).show()
                fetchConfig()
            } else {
                Toast.makeText(this@MainActivity, "Failed to save configuration", Toast.LENGTH_LONG).show()
            }
        }
    }

    private fun toggleRunner() {
        if (serverUrl.isEmpty()) return
        val isRunning = binding.tvRunnerStatus.text.toString() == "Running"
        val endpoint = if (isRunning) "stop" else "start"
        
        binding.btnStartStop.isEnabled = false

        CoroutineScope(Dispatchers.Main).launch {
            val success = withContext(Dispatchers.IO) {
                val request = Request.Builder()
                    .url("$serverUrl/api/$endpoint")
                    .post("".toRequestBody())
                    .build()
                try {
                    client.newCall(request).execute().use { response ->
                        if (response.isSuccessful && response.body != null) {
                            val apiResp = gson.fromJson(response.body!!.string(), ApiResponse::class.java)
                            apiResp.success
                        } else false
                    }
                } catch (e: Exception) {
                    false
                }
            }

            binding.btnStartStop.isEnabled = true
            if (success) {
                refreshData(silent = true)
            } else {
                Toast.makeText(this@MainActivity, "Failed to toggle runner state", Toast.LENGTH_SHORT).show()
            }
        }
    }

    private fun updateRunnerStatusUi(isRunning: Boolean) {
        if (isRunning) {
            binding.tvRunnerStatus.text = "Running"
            binding.statusIndicator.setBackgroundResource(R.drawable.status_dot_running)
            binding.btnStartStop.text = "Stop Keep-Alive"
            binding.btnStartStop.backgroundTintList = ColorStateList.valueOf(
                ContextCompat.getColor(this, R.color.state_failed)
            )
        } else {
            binding.tvRunnerStatus.text = "Stopped"
            binding.statusIndicator.setBackgroundResource(R.drawable.status_dot_stopped)
            binding.btnStartStop.text = "Start Keep-Alive"
            binding.btnStartStop.backgroundTintList = ColorStateList.valueOf(
                ContextCompat.getColor(this, R.color.state_connected)
            )
        }
    }
}
